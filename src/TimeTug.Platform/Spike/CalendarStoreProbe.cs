#pragma warning disable
// THROWAWAY spike probe for docs/spikes/2026-11-windows-calendar-store.md. Not merged. Counts only; no titles, attendees, locations or account names.
using Windows.ApplicationModel.Appointments;

namespace TimeTug.Platform.Spike;

public static class CalendarStoreProbe
{
    public static async Task RunAsync(Action<string> note, bool fidelity)
    {
        AppointmentStore? store = null;
        try
        {
            store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
            note(store == null ? "store(AllCalendarsReadOnly): returned null (access refused without an exception)" : "store(AllCalendarsReadOnly): opened");
        }
        catch (Exception ex) { note($"store(AllCalendarsReadOnly): refused exception={ex.GetType().FullName} hresult=0x{ex.HResult:X8} message=\"{WamProbe.Redact(ex.Message, 200)}\""); }
        if (store == null) return;

        await DumpAsync(store, note, "all", null);
        if (fidelity) await FidelityAsync(store, note);
    }

    private static async Task DumpAsync(AppointmentStore store, Action<string> note, string label, string? onlyLocalId)
    {
        var cals = await store.FindAppointmentCalendarsAsync(FindAppointmentCalendarsOptions.IncludeHidden);
        note($"{label}: calendars={cals.Count}");
        var start = DateTimeOffset.Now;
        foreach (var c in cals)
        {
            if (onlyLocalId != null && c.LocalId != onlyLocalId) continue;
            var opts = new FindAppointmentsOptions { MaxCount = 500, IncludeHidden = true };
            opts.CalendarIds.Add(c.LocalId);
            foreach (var p in AppointmentProperties.DefaultProperties) opts.FetchProperties.Add(p);
            AddExtra(opts);
            IReadOnlyList<Appointment> items;
            try { items = await store.FindAppointmentsAsync(start, TimeSpan.FromDays(14), opts); }
            catch (Exception ex) { note($"{label}: calendar provider={WamProbe.Redact(c.SourceDisplayName, 60)} find failed {ex.GetType().Name} 0x{ex.HResult:X8}"); continue; }
            int allDay = 0, withLoc = 0, withOrg = 0, recurring = 0, withLink = 0, withSubject = 0, canceled = 0;
            foreach (var a in items)
            {
                if (a.AllDay) allDay++;
                if (!string.IsNullOrEmpty(a.Location)) withLoc++;
                if (a.Organizer != null) withOrg++;
                if (a.Recurrence != null) recurring++;
                try { if (!string.IsNullOrEmpty(a.OnlineMeetingLink)) withLink++; } catch { }
                if (!string.IsNullOrEmpty(a.Subject)) withSubject++;
                if (a.IsCanceledMeeting) canceled++;
            }
            note($"{label}: calendar provider=\"{WamProbe.Redact(c.SourceDisplayName, 60)}\" hidden={c.IsHidden} accountBound={!string.IsNullOrEmpty(c.UserDataAccountId)} otherAppRead={c.OtherAppReadAccess} otherAppWrite={c.OtherAppWriteAccess} appointments14d={items.Count} allDay={allDay} withLocation={withLoc} withOrganizer={withOrg} withSeriesInfo={recurring} withOnlineLink={withLink} withSubject={withSubject} canceled={canceled}");
        }
    }

    private static async Task FidelityAsync(AppointmentStore ro, Action<string> note)
    {
        AppointmentStore? rw = null;
        AppointmentCalendar? cal = null;
        try
        {
            rw = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AppCalendarsReadWrite);
            if (rw == null) { note("fidelity: AppCalendarsReadWrite returned null"); return; }
            cal = await rw.CreateAppointmentCalendarAsync("TimeTugSpike");
            cal.OtherAppReadAccess = AppointmentCalendarOtherAppReadAccess.Full;
            await cal.SaveAsync();
            var tomorrow = DateTimeOffset.Now.Date.AddDays(1);
            var series = new Appointment
            {
                Subject = "Spike series", Location = "Room 1", StartTime = new DateTimeOffset(tomorrow.AddHours(9), DateTimeOffset.Now.Offset), Duration = TimeSpan.FromMinutes(30),
                Organizer = new AppointmentOrganizer { DisplayName = "Spike Organizer", Address = "organizer@example.invalid" },
                Recurrence = new AppointmentRecurrence { Unit = AppointmentRecurrenceUnit.Daily, Interval = 1, Occurrences = 5 },
            };
            try { series.OnlineMeetingLink = "https://example.invalid/meet"; } catch (Exception ex) { note($"fidelity: OnlineMeetingLink not settable {ex.GetType().Name}"); }
            await cal.SaveAppointmentAsync(series);
            var allDay = new Appointment { Subject = "Spike all day", AllDay = true, StartTime = new DateTimeOffset(tomorrow, DateTimeOffset.Now.Offset), Duration = TimeSpan.FromDays(1) };
            await cal.SaveAppointmentAsync(allDay);
            var single = new Appointment { Subject = "Spike single", StartTime = new DateTimeOffset(tomorrow.AddHours(14.5), DateTimeOffset.Now.Offset), Duration = TimeSpan.FromHours(1), Location = "https://example.invalid/join" };
            await cal.SaveAppointmentAsync(single);
            note("fidelity: wrote one calendar with a 5-occurrence daily series (organizer, location, online link), one all-day and one single event");

            await DumpAsync(ro, note, "fidelity-readback-via-AllCalendarsReadOnly", cal.LocalId);

            var opts = new FindAppointmentsOptions { MaxCount = 100 };
            opts.CalendarIds.Add(cal.LocalId);
            foreach (var p in AppointmentProperties.DefaultProperties) opts.FetchProperties.Add(p);
            AddExtra(opts);
            var got = await ro.FindAppointmentsAsync(DateTimeOffset.Now, TimeSpan.FromDays(14), opts);
            note($"fidelity: default-property list = {string.Join(",", AppointmentProperties.DefaultProperties)}");
            note($"fidelity: readback occurrences={got.Count} (expected 7 = 5 series + 1 all-day + 1 single)");
            foreach (var a in got.OrderBy(x => x.StartTime))
                note($"fidelity: occurrence start={a.StartTime:yyyy-MM-dd HH:mm zzz} durMin={a.Duration.TotalMinutes} allDay={a.AllDay} hasOrganizer={a.Organizer != null} hasLocation={!string.IsNullOrEmpty(a.Location)} hasSeries={a.Recurrence != null} onlineLink={(string.IsNullOrEmpty(SafeLink(a)) ? "empty" : "present")} subjectPrefix={(a.Subject ?? "").Split(' ').FirstOrDefault()} instanceHasOriginalStart={a.OriginalStartTime != null} isInstance={a.IsOrganizedByUser}");
        }
        catch (Exception ex) { note($"fidelity: failed {ex.GetType().FullName} hresult=0x{ex.HResult:X8} message=\"{WamProbe.Redact(ex.Message, 200)}\""); }
        finally
        {
            try { if (cal != null) { await cal.DeleteAsync(); note("fidelity: test calendar deleted"); } } catch (Exception ex) { note($"fidelity: delete failed {ex.GetType().Name}"); }
        }
    }

    private static void AddExtra(FindAppointmentsOptions o)
    {
        foreach (var p in new[] { AppointmentProperties.Organizer, AppointmentProperties.OnlineMeetingLink, AppointmentProperties.Recurrence, AppointmentProperties.IsCanceledMeeting, AppointmentProperties.Invitees, AppointmentProperties.Details, AppointmentProperties.Uri, AppointmentProperties.OriginalStartTime, AppointmentProperties.BusyStatus })
            if (!o.FetchProperties.Contains(p)) o.FetchProperties.Add(p);
    }

    private static string SafeLink(Appointment a) { try { return a.OnlineMeetingLink ?? ""; } catch { return ""; } }
}

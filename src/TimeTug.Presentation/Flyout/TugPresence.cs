using TimeTug.Presentation.Tug;

namespace TimeTug.Presentation.Flyout;

/// <summary>Reports whether the full-screen tug is up, from the controller's plan. A toast is not a tug window, so it does not count.</summary>
public sealed class TugPresence : ITugPresence, IDisposable
{
    private readonly TugController _controller;

    public TugPresence(TugController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        controller.PlanChanged += OnPlan;
    }

    public bool IsTugShowing => _controller.Plan.Mode == TugMode.FullScreen;

    public event Action? Changed;

    public void Dispose() => _controller.PlanChanged -= OnPlan;

    private void OnPlan(TugPlan plan) => Changed?.Invoke();
}

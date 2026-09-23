namespace ArcEngine.Engine.UI;

/// <summary>
/// Minimal one-way data-binding primitive. Gameplay code writes to
/// <see cref="Value"/>; UI code subscribes via <see cref="Bind"/> (or attaches
/// to <see cref="Changed"/> directly) and rewires whatever widget field mirrors it.
///
/// Deliberately simple: no debouncing, no batching, no thread safety —
/// designed for the single-threaded game loop. Suitable for health bars,
/// score readouts, config toggles.
/// </summary>
public class Observable<T>
{
    private T _value;

    public Observable(T initial) { _value = initial; }

    public T Value
    {
        get => _value;
        set
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(_value, value)) return;
            _value = value;
            Changed?.Invoke(value);
        }
    }

    /// <summary>Fired whenever <see cref="Value"/> is replaced with a non-equal value.</summary>
    public event Action<T>? Changed;

    /// <summary>
    /// Subscribe <paramref name="apply"/> and immediately call it with the current
    /// value so the widget mirrors the state without waiting for the next change.
    /// Returns a token: dispose it to unsubscribe.
    /// </summary>
    public IDisposable Bind(Action<T> apply)
    {
        Changed += apply;
        apply(_value);
        return new Subscription(() => Changed -= apply);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        public Subscription(Action unsubscribe) { _unsubscribe = unsubscribe; }
        public void Dispose() => _unsubscribe();
    }
}

/// <summary>
/// Widget-friendly extensions for the common patterns: bind an
/// <see cref="Observable{T}"/> to a text widget or a slider without writing the
/// glue boilerplate every time.
/// </summary>
public static class ObservableBindings
{
    public static IDisposable BindTo(this Observable<string> obs, Text text)
        => obs.Bind(v => text.Value = v);

    public static IDisposable BindTo(this Observable<float> obs, Text text, string format = "F0")
        => obs.Bind(v => text.Value = v.ToString(format, System.Globalization.CultureInfo.InvariantCulture));

    public static IDisposable BindTo(this Observable<int> obs, Text text)
        => obs.Bind(v => text.Value = v.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Two-way for sliders: pull → set slider, push slider drags back into the observable.</summary>
    public static IDisposable BindTwoWay(this Observable<float> obs, Slider slider)
    {
        var sub = obs.Bind(v => slider.Value = v);
        slider.OnValueChanged += v => obs.Value = v;
        return sub;
    }
}

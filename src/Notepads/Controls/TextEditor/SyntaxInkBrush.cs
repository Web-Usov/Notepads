namespace Notepads.Controls.TextEditor
{
    using System;
    using Microsoft.Graphics.Canvas.Effects;
    using Windows.UI;
    using Windows.UI.Composition;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Media;

    // Limit the native foreground's RGB channels to the token color. RichEdit still
    // shapes and draws every glyph; no character formatting enters its undo history.
    internal sealed class SyntaxInkBrush : XamlCompositionBrushBase
    {
        private readonly Color _color;
        private readonly bool _darkTheme;
        private CompositionBackdropBrush _backdrop;

        internal SyntaxInkBrush(Color color, bool darkTheme)
        {
            _color = color;
            _darkTheme = darkTheme;
            FallbackColor = Color.FromArgb(64, color.R, color.G, color.B);
        }

        protected override void OnConnected()
        {
            if (CompositionBrush != null) return;
            var compositor = Window.Current.Compositor;
            if (CompositionCapabilities.GetForCurrentView().AreEffectsSupported())
            {
                try
                {
                    using (var tint = new ColorSourceEffect { Color = _color })
                    using (var effect = new BlendEffect
                    {
                        Mode = _darkTheme ? BlendEffectMode.Darken : BlendEffectMode.Lighten,
                        Background = new CompositionEffectSourceParameter("Ink"),
                        Foreground = tint
                    })
                    using (var factory = compositor.CreateEffectFactory(effect))
                    {
                        var brush = factory.CreateBrush();
                        _backdrop = compositor.CreateBackdropBrush();
                        brush.SetSourceParameter("Ink", _backdrop);
                        CompositionBrush = brush;
                    }
                    return;
                }
                catch (Exception)
                {
                    // Older graphics drivers may reject an otherwise supported effect.
                    _backdrop?.Dispose();
                    _backdrop = null;
                }
            }

            CompositionBrush = compositor.CreateColorBrush(FallbackColor);
        }

        protected override void OnDisconnected()
        {
            CompositionBrush?.Dispose();
            CompositionBrush = null;
            _backdrop?.Dispose();
            _backdrop = null;
        }
    }
}

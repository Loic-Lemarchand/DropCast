using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;
using Color = Android.Graphics.Color;
using PaintStyle = Android.Graphics.Paint.Style;
using PaintJoin = Android.Graphics.Paint.Join;
using PaintCap = Android.Graphics.Paint.Cap;

namespace DropCast.Android.Platforms;

/// <summary>
/// A TextView that draws outlined "meme-style" text:
/// white fill with a multi-pass black stroke outline,
/// matching the desktop CustomLabel look (Arial Black + outline).
/// Uses sans-serif-black bold typeface (≈ Arial Black on Android).
/// </summary>
public class MemeTextView : TextView
{
    private readonly float _strokeWidth;

    public MemeTextView(Context context) : base(context)
    {
        Typeface? impactFont = null;
        try { impactFont = Typeface.CreateFromAsset(context.Assets!, "impact.ttf"); }
        catch { /* fallback below */ }
        var tf = impactFont ?? Typeface.Create("sans-serif-black", TypefaceStyle.Bold);
        SetTypeface(tf, TypefaceStyle.Normal);
        SetTextColor(Color.White);
        SetAllCaps(true);
        Gravity = GravityFlags.Center;
        SetLineSpacing(0, 1.05f);
        _strokeWidth = TypedValue.ApplyDimension(ComplexUnitType.Dip, 3f,
            context.Resources!.DisplayMetrics);
    }

    protected override void OnDraw(Canvas? canvas)
    {
        if (canvas == null) { base.OnDraw(canvas); return; }

        int savedColor = CurrentTextColor;
        var textPaint = Paint;

        // Pass 1: Black outer shadow (wide stroke, like desktop's 8px shadow)
        SetTextColor(Color.Black);
        textPaint.SetStyle(PaintStyle.Stroke);
        textPaint.StrokeWidth = _strokeWidth * 2.5f;
        textPaint.StrokeJoin = PaintJoin.Round;
        textPaint.StrokeCap = PaintCap.Round;
        base.OnDraw(canvas);

        // Pass 2: Dark outline (narrower, matching desktop's #0B091A 5px pen)
        SetTextColor(Color.Rgb(11, 9, 26));
        textPaint.StrokeWidth = _strokeWidth * 1.5f;
        base.OnDraw(canvas);

        // Pass 3: White fill (text interior)
        SetTextColor(new Color(savedColor));
        textPaint.SetStyle(PaintStyle.Fill);
        base.OnDraw(canvas);
    }
}

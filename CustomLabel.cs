using static System.Net.Mime.MediaTypeNames;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection.Emit;
using Label = System.Windows.Forms.Label;
using System;
using System.Drawing.Text;

namespace DropCast
{

    public class CustomLabel : Label
    {
        public CustomLabel()
        {
            OutlineForeColor = Color.Black; // ✅ Contour noir (pas vert)
            OutlineWidth = 3f;
            ForeColor = Color.White;  // ✅ Texte bien blanc
            AutoSize = false;
            DoubleBuffered = true;
            TextAlign = ContentAlignment.MiddleCenter;
            BackColor = Color.Transparent; // ✅ Fond totalement transparent
        }

        public Color OutlineForeColor { get; set; }
        public float OutlineWidth { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width, this.Height);
            StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.LineLimit
            };

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddString(Text, Font.FontFamily, (int)Font.Style, Font.Size, rect, format);

                // ✅ Ombre externe plus large (pour un meilleur effet visuel)
                using (Pen shadowPen = new Pen(Color.Black, 8f) { LineJoin = LineJoin.Round })
                {
                    e.Graphics.DrawPath(shadowPen, path);
                }

                // ✅ Contour principal légèrement plus fin
                using (Pen outlinePen = new Pen(Color.FromArgb(11, 9, 26), 5f) { LineJoin = LineJoin.Round })
                {
                    e.Graphics.DrawPath(outlinePen, path);
                }

                // ✅ Texte blanc rempli à l'intérieur du contour
                using (Brush textBrush = new SolidBrush(ForeColor))
                {
                    e.Graphics.FillPath(textBrush, path);
                }
            }
        }


        public void UpdateSize(int newWidth)
        {
            this.Width = newWidth;
            AdjustHeight();
        }

        public void AdjustHeight()
        {
            using (Graphics g = CreateGraphics())
            {
                Size textSize = TextRenderer.MeasureText(Text, Font, new Size(this.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

                this.Height = textSize.Height;
            }
        }
    }
}

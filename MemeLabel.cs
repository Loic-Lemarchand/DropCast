using static System.Net.Mime.MediaTypeNames;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection.Emit;


namespace LiveChatDesktop { 

    public class CustomLabel : Label
    {
        public CustomLabel()
        {
            OutlineForeColor = Color.Black; // Contour noir
            OutlineWidth = 3; // Épaisseur du contour
            ForeColor = Color.White; // Texte blanc
            MaxWidth = 0; // Par défaut, il n'y a pas de limite
        }

        public Color OutlineForeColor { get; set; }
        public float OutlineWidth { get; set; }
        public float MaxWidth { get; set; }  // Nouvelle propriété pour définir la largeur maximale du texte

        protected override void OnPaint(PaintEventArgs e)
        {
            // Remplir l'arrière-plan avec la couleur de fond
            e.Graphics.FillRectangle(new SolidBrush(BackColor), ClientRectangle);

            using (GraphicsPath gp = new GraphicsPath())
            using (Pen outline = new Pen(OutlineForeColor, OutlineWidth) { LineJoin = LineJoin.Round })
            using (StringFormat sf = new StringFormat())
            using (Brush foreBrush = new SolidBrush(ForeColor))
            {
                // Centrer le texte horizontalement et verticalement
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                // Calculer dynamiquement le rectangle pour ajuster la largeur en fonction de MaxWidth
                // Si MaxWidth est 0, cela signifie qu'il n'y a pas de limite.
                float width = MaxWidth > 0 ? MaxWidth : this.ClientSize.Width;
                RectangleF textRectangle = new RectangleF(0, 0, width, this.ClientSize.Height);

                // Calculer la taille du texte et appliquer un retour à la ligne automatique si nécessaire
                e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

                // Ajouter le texte avec retour à la ligne automatique
                gp.AddString(Text, Font.FontFamily, (int)Font.Style, Font.Size, textRectangle, sf);

                // Dessiner le contour du texte
                e.Graphics.DrawPath(outline, gp);
                // Dessiner le texte
                e.Graphics.FillPath(foreBrush, gp);
            }
        }
    }
}

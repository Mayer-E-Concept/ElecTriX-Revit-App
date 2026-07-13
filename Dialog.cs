// Dialog.cs — nur WPF, keine Revit DB imports
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace METools
{
    public class DistSettings
    {
        public int    Rows    = 2;
        public int    Cols    = 3;
        public double SpX     = 2.0;
        public double SpY     = 2.0;
        public double Rot     = 0.0;
        public bool   Replace = false;
        public bool   Center  = true;
        public string Pattern = "Grid";
    }

    public class DistDialog : Window
    {
        public DistSettings Result = new DistSettings();

        private Slider _sRows, _sCols, _sSpX, _sSpY, _sRot;
        private TextBox _tRows, _tCols, _tSpX, _tSpY, _tRot;
        private ComboBox _cPat;
        private Canvas _cv;
        private TextBlock _lTotal, _lW, _lD;
        private CheckBox _chkCtr, _chkDel;
        private bool _busy;
        private double _roomW, _roomD;
        private bool _hasRoom;

        public DistDialog(string roomName, double roomW, double roomD, bool hasRoom)
        {
            _roomW = roomW; _roomD = roomD; _hasRoom = hasRoom;
            Title = "Symmetrische Objektverteilung";
            Width = 430; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(240, 245, 245));

            var root = new StackPanel { Margin = new Thickness(12) };
            Content = root;

            // Banner
            var bnr = new Border { Background = new SolidColorBrush(Color.FromRgb(224, 240, 240)), BorderBrush = new SolidColorBrush(Color.FromRgb(150, 210, 205)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10,6,10,6), Margin = new Thickness(0,0,0,8) };
            var bnrSp = new StackPanel();
            var bnrT = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(15, 143, 135)) };
            var bnrD = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(68,68,68)), Margin = new Thickness(0,2,0,0) };
            if (hasRoom && !string.IsNullOrEmpty(roomName)) { bnrT.Text = "Raum: " + roomName; bnrD.Text = $"B: {roomW:F1} m  ·  T: {roomD:F1} m"; }
            else { bnrT.Text = "Kein Raum erkannt"; bnrD.Text = "Abstände manuell einstellen"; }
            bnrSp.Children.Add(bnrT); bnrSp.Children.Add(bnrD); bnr.Child = bnrSp;
            root.Children.Add(bnr);

            // Raster
            double defSpX = hasRoom && roomW > 0 ? Math.Round(Math.Min(roomW/3,4),1) : 2.0;
            double defSpY = hasRoom && roomD > 0 ? Math.Round(Math.Min(roomD/2,4),1) : 2.0;
            root.Children.Add(Grp("Raster", Tbl(new[]{
                ("Reihen",        Row(out _sRows, out _tRows, 1,  20, 2,      true)),
                ("Spalten",       Row(out _sCols, out _tCols, 1,  20, 3,      true)),
                ("Abstand X (m)", Row(out _sSpX,  out _tSpX,  0.1,20, defSpX, false)),
                ("Abstand Y (m)", Row(out _sSpY,  out _tSpY,  0.1,20, defSpY, false)),
            })));

            // Ausrichtung
            _cPat = new ComboBox { FontSize=12, Height=26, Margin=new Thickness(0,4,0,0) };
            foreach (var s in new[]{"Raster (Grid)","Kreis / Ring","Hexagonal","Diagonal versetzt"}) _cPat.Items.Add(s);
            _cPat.SelectedIndex = 0;
            _cPat.SelectionChanged += (_,__) => Draw();
            root.Children.Add(Grp("Ausrichtung", Tbl(new[]{
                ("Drehung (°)", Row(out _sRot, out _tRot, 0, 360, 0, true)),
                ("Muster",      _cPat),
            })));

            // Preview
            _cv = new Canvas { Width=394, Height=190, Background=Brushes.White };
            _lTotal = Val(); _lW = Val(); _lD = Val();
            var inf = new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Center, Margin=new Thickness(0,6,0,0) };
            inf.Children.Add(Bdg("Objekte: ",_lTotal)); inf.Children.Add(Bdg("Breite: ",_lW)); inf.Children.Add(Bdg("Tiefe: ",_lD));
            var pvSp = new StackPanel(); pvSp.Children.Add(_cv); pvSp.Children.Add(inf);
            root.Children.Add(Grp("Vorschau (Draufsicht)", pvSp));

            // Options
            _chkCtr = new CheckBox { Content="Raummitte als Ursprung", FontSize=12, IsChecked=true, Margin=new Thickness(0,0,0,4) };
            _chkDel = new CheckBox { Content="Ursprüngliche Auswahl löschen", FontSize=12 };
            var optSp = new StackPanel(); optSp.Children.Add(_chkCtr); optSp.Children.Add(_chkDel);
            root.Children.Add(Grp("Optionen", optSp));

            // Buttons
            var br = new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,6,0,0) };
            var bCan = new Button { Content="Abbrechen", Height=30, MinWidth=90, Margin=new Thickness(0,0,8,0), FontSize=12,
                Background=MeToolsTheme.BrBtnBg, Foreground=MeToolsTheme.BrText,
                BorderBrush=MeToolsTheme.BrBtnBorder, BorderThickness=new Thickness(1),
                Cursor=Cursors.Hand, Template=MeToolsWindowBase.RoundedBtnTemplate() };
            var bOK  = new Button { Content="Verteilen", Height=30, MinWidth=90, FontSize=12, FontWeight=FontWeights.SemiBold, Background=new SolidColorBrush(Color.FromRgb(15, 143, 135)), Foreground=Brushes.White, IsDefault=true,
                BorderBrush=new SolidColorBrush(Color.FromRgb(15, 143, 135)), BorderThickness=new Thickness(1),
                Cursor=Cursors.Hand, Template=MeToolsWindowBase.RoundedBtnTemplate() };
            bCan.Click += (_,__) => { DialogResult=false; Close(); };
            bOK.Click  += (_,__) =>
            {
                Result.Rows    = (int)(_sRows?.Value ?? 2);
                Result.Cols    = (int)(_sCols?.Value ?? 3);
                Result.SpX     = _sSpX?.Value ?? 2.0;
                Result.SpY     = _sSpY?.Value ?? 2.0;
                Result.Rot     = _sRot?.Value ?? 0.0;
                Result.Replace = _chkDel?.IsChecked == true;
                Result.Center  = _chkCtr?.IsChecked == true;
                Result.Pattern = (_cPat?.SelectedIndex) switch { 1=>"Circle", 2=>"Hex", 3=>"Diag", _=>"Grid" };
                DialogResult=true; Close();
            };
            br.Children.Add(bCan); br.Children.Add(bOK);
            root.Children.Add(br);

            Loaded += (_,__) => Draw();
        }

        private UIElement Row(out Slider sl, out TextBox tb, double mn, double mx, double val, bool snap)
        {
            var g = new Grid(); g.Height=30;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(62) });
            sl = new Slider { Minimum=mn, Maximum=mx, Value=val, IsSnapToTickEnabled=snap, TickFrequency=snap?1:0.1, VerticalAlignment=VerticalAlignment.Center };
            tb = new TextBox { Width=58, Height=24, VerticalContentAlignment=VerticalAlignment.Center, HorizontalContentAlignment=HorizontalAlignment.Center, FontSize=12, Text=snap?((int)val).ToString():val.ToString("F2") };
            var slR=sl; var tbR=tb;
            sl.ValueChanged += (_,__) => { if(_busy)return; _busy=true; tbR.Text=snap?((int)slR.Value).ToString():slR.Value.ToString("F2"); _busy=false; Draw(); };
            tb.TextChanged += (_,__) => { if(_busy)return; if(double.TryParse(tbR.Text.Replace(',','.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v)) { _busy=true; slR.Value=Math.Max(mn,Math.Min(mx,v)); _busy=false; Draw(); } };
            Grid.SetColumn(sl,0); Grid.SetColumn(tb,1);
            g.Children.Add(sl); g.Children.Add(tb);
            return g;
        }

        private Grid Tbl((string lbl, UIElement el)[] rows)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(115) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            for(int i=0;i<rows.Length;i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
                var lbl=new TextBlock{Text=rows[i].lbl,FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,4,8,4)};
                Grid.SetRow(lbl,i); Grid.SetColumn(lbl,0);
                Grid.SetRow(rows[i].el,i); Grid.SetColumn(rows[i].el,1);
                g.Children.Add(lbl); g.Children.Add(rows[i].el);
            }
            return g;
        }
        private GroupBox Grp(string h, UIElement c) => new GroupBox { Header=h, Content=c, Margin=new Thickness(0,0,0,8), Padding=new Thickness(10,6,10,8), FontSize=12, FontWeight=FontWeights.SemiBold };
        private Border Bdg(string lbl, TextBlock v) { var sp=new StackPanel{Orientation=Orientation.Horizontal}; sp.Children.Add(new TextBlock{Text=lbl,FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(68,68,68))}); sp.Children.Add(v); return new Border{Background=new SolidColorBrush(Color.FromRgb(224, 240, 240)),CornerRadius=new CornerRadius(4),Padding=new Thickness(8,3,8,3),Margin=new Thickness(3,0,3,0),Child=sp}; }
        private TextBlock Val() => new TextBlock { FontSize=11, Foreground=new SolidColorBrush(Color.FromRgb(15, 143, 135)), FontWeight=FontWeights.SemiBold };

        private int    R   => Math.Max(1,(int)(_sRows?.Value??2));
        private int    C   => Math.Max(1,(int)(_sCols?.Value??3));
        private double SX  => _sSpX?.Value??2.0;
        private double SY  => _sSpY?.Value??2.0;
        private double ROT => _sRot?.Value??0.0;
        private string PAT => (_cPat?.SelectedIndex) switch { 1=>"Circle", 2=>"Hex", 3=>"Diag", _=>"Grid" };

        private void Draw()
        {
            try
            {
                if(_cv==null) return;
                _cv.Children.Clear();
                var pts = Pts(PAT,R,C,SX,SY,ROT*Math.PI/180.0);
                if(pts.Count==0) return;
                double cW=_cv.Width, cH=_cv.Height, pad=28;
                double mnX=pts.Min(p=>p.X),mxX=pts.Max(p=>p.X),mnY=pts.Min(p=>p.Y),mxY=pts.Max(p=>p.Y);
                double sc=Math.Min(Math.Min((cW-pad*2)/Math.Max(mxX-mnX,0.01),(cH-pad*2)/Math.Max(mxY-mnY,0.01)),36);
                double cx=cW/2, cy=cH/2, mx2=(mnX+mxX)/2, my2=(mnY+mxY)/2;

                if(_hasRoom && _roomW>0 && _roomD>0)
                {
                    double rw=_roomW*sc, rd=_roomD*sc;
                    var rect=new Rectangle{Width=Math.Max(rw,4),Height=Math.Max(rd,4),Stroke=new SolidColorBrush(Color.FromArgb(80,15, 143, 135)),StrokeThickness=1,Fill=new SolidColorBrush(Color.FromArgb(12,15, 143, 135)),StrokeDashArray=new DoubleCollection(new double[]{4,3})};
                    Canvas.SetLeft(rect,cx-rw/2); Canvas.SetTop(rect,cy-rd/2); _cv.Children.Add(rect);
                }
                for(int g2=-200;g2<=200;g2+=20)
                {
                    _cv.Children.Add(new Line{X1=cx+g2,Y1=0,X2=cx+g2,Y2=cH,Stroke=new SolidColorBrush(Color.FromArgb(15,0,0,0)),StrokeThickness=0.5});
                    _cv.Children.Add(new Line{X1=0,Y1=cy+g2,X2=cW,Y2=cy+g2,Stroke=new SolidColorBrush(Color.FromArgb(15,0,0,0)),StrokeThickness=0.5});
                }
                foreach(var p in pts)
                {
                    double px=cx+(p.X-mx2)*sc, py=cy+(p.Y-my2)*sc;
                    var o=new Ellipse{Width=14,Height=14,Fill=new SolidColorBrush(Color.FromArgb(35,15, 143, 135)),Stroke=new SolidColorBrush(Color.FromArgb(100,11, 111, 104)),StrokeThickness=0.8};
                    Canvas.SetLeft(o,px-7); Canvas.SetTop(o,py-7);
                    var d=new Ellipse{Width=7,Height=7,Fill=new SolidColorBrush(Color.FromRgb(15, 143, 135))};
                    Canvas.SetLeft(d,px-3.5); Canvas.SetTop(d,py-3.5);
                    var st=new Line{X1=px,Y1=py-3.5,X2=px,Y2=py-9,Stroke=new SolidColorBrush(Color.FromRgb(11, 111, 104)),StrokeThickness=1.2};
                    _cv.Children.Add(o); _cv.Children.Add(d); _cv.Children.Add(st);
                }
                if(_lTotal!=null) _lTotal.Text=pts.Count.ToString();
                if(_lW!=null) _lW.Text=((C-1)*SX).ToString("F1")+" m";
                if(_lD!=null) _lD.Text=((R-1)*SY).ToString("F1")+" m";
            }
            catch{}
        }

        private List<Point> Pts(string pat, int rows, int cols, double spx, double spy, double rot)
        {
            var raw=new List<(double x,double y)>();
            switch(pat)
            {
                case "Circle": int n=(rows+cols)*2; double r=Math.Min(rows*spx,cols*spy)/2; for(int i=0;i<n;i++) raw.Add((Math.Cos(2*Math.PI*i/n)*r,Math.Sin(2*Math.PI*i/n)*r)); break;
                case "Hex":    double hox=cols*spx/2,hoy=rows*spy*0.866/2; for(int i=0;i<rows;i++) for(int j=0;j<cols;j++) raw.Add((j*spx+(i%2)*spx*0.5-hox,i*spy*0.866-hoy)); break;
                case "Diag":   double dox=(cols-1)*spx/2,doy=(rows-1)*spy/2; for(int i=0;i<rows;i++) for(int j=0;j<cols;j++) raw.Add((j*spx-dox+i*spx*0.3,i*spy-doy)); break;
                default:       double gox=(cols-1)*spx/2,goy=(rows-1)*spy/2; for(int i=0;i<rows;i++) for(int j=0;j<cols;j++) raw.Add((j*spx-gox,i*spy-goy)); break;
            }
            return raw.Select(p=>new Point(p.x*Math.Cos(rot)-p.y*Math.Sin(rot),p.x*Math.Sin(rot)+p.y*Math.Cos(rot))).ToList();
        }
    }
}

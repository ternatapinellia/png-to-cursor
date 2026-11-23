using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class AlphaPendant : Form
{
    const int WS_EX_LAYERED=0x80000, WS_EX_TRANSPARENT=0x20, WS_EX_TOOLWINDOW=0x80, WS_EX_NOACTIVATE=0x08000000;
    const byte AC_SRC_OVER=0, AC_SRC_ALPHA=1;
    const int ULW_ALPHA=2;
    const int Segments=8;
    [StructLayout(LayoutKind.Sequential, Pack=1)] struct BLENDFUNCTION { public byte BlendOp,BlendFlags,SourceConstantAlpha,AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX,CY; }
    [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT pt; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dst,ref POINT d,ref SIZE s,IntPtr src,ref POINT p,int key,ref BLENDFUNCTION b,int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr h);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

    readonly Timer timer=new Timer();
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    Image idle,longIdle,swing;
    FrameDimension idleDim,longDim,swingDim;
    int idleFrames,longFrames,swingFrames,idleFrame,longFrame,swingFrame; int[] idleDurations,longDurations,swingDurations;
    float[] idleHeadX,idleHeadY,longHeadX,longHeadY,swingHeadX,swingHeadY;
    float[] nx=new float[Segments+1], ny=new float[Segments+1];
    float[] px=new float[Segments+1], py=new float[Segments+1];
    float anchorX=250f, anchorY=60f;
    float ballX, ballY;
    float cursorSpeed, lastMouseX;
    DateTime lastFrame=DateTime.UtcNow, lastMove=DateTime.UtcNow;
    int characterHeight=76, ropeLength=49;
    float characterRotationScale=-1f; float headOffsetX=0f, headOffsetY=0f;
    Color ropeColor=Color.Black;
    bool paused;

    public AlphaPendant()
    {
        LoadConfig();
        idle=LoadImage("idle.gif");
        longIdle=LoadImage("long_idle.gif");
        swing=LoadImage("swing.gif");
        if(idle==null) throw new FileNotFoundException("assets/idle.gif");
        if(longIdle==null) longIdle=idle;
        if(swing==null) swing=idle;
        idleDim=Dimension(idle,out idleFrames); idleDurations=ReadDurations(idle,idleFrames);
        longDim=Dimension(longIdle,out longFrames); longDurations=ReadDurations(longIdle,longFrames);
        swingDim=Dimension(swing,out swingFrames); swingDurations=ReadDurations(swing,swingFrames);
        FindHeadAnchors(idle,idleDim,idleFrames,out idleHeadX,out idleHeadY);
        FindHeadAnchors(longIdle,longDim,longFrames,out longHeadX,out longHeadY);
        FindHeadAnchors(swing,swingDim,swingFrames,out swingHeadX,out swingHeadY);
        FormBorderStyle=FormBorderStyle.None;
        ShowInTaskbar=false;
        TopMost=true;
        Size=new Size(540,560);
        Point p=Cursor.Position;
        lastMouseX=p.X;
        Location=new Point(p.X-250,p.Y-60);
        InitRope();
        timer.Interval=10;
        timer.Tick+=Tick;
        Shown+=delegate { timer.Start(); Render(); };
    }

    protected override CreateParams CreateParams
    {
        get { CreateParams p=base.CreateParams; p.ExStyle|=WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE; return p; }
    }
    protected override bool ShowWithoutActivation { get { return true; } }

    public int CharacterHeight { get { return characterHeight; } set { characterHeight=Math.Max(32,Math.Min(500,value)); SaveConfig(); } }
    public int RopeLength { get { return ropeLength; } set { ropeLength=Math.Max(0,Math.Min(480,value)); InitRope(); SaveConfig(); } }
    public Color RopeColor { get { return ropeColor; } set { ropeColor=value; SaveConfig(); } }
    public float HeadOffsetX { get { return headOffsetX; } set { headOffsetX=value; SaveConfig(); } }
    public float HeadOffsetY { get { return headOffsetY; } set { headOffsetY=value; SaveConfig(); } }
    public void TogglePause() { paused=!paused; }

    Image LoadImage(string name) { string p=Path.Combine(root,"assets",name); return File.Exists(p)?Image.FromFile(p):null; }
    static FrameDimension Dimension(Image image,out int count) { FrameDimension d=new FrameDimension(image.FrameDimensionsList[0]); count=image.GetFrameCount(d); return d; }
    static int Next(int v,int n) { return n<=0?0:(v+1)%n; }
    static int[] ReadDurations(Image image,int frames)
    {
        int[] durations=new int[Math.Max(1,frames)];
        for(int i=0;i<durations.Length;i++) durations[i]=100;
        try
        {
            var prop=image.GetPropertyItem(0x5100);
            if(prop!=null && prop.Value!=null)
                for(int i=0;i<durations.Length && i*4+3<prop.Value.Length;i++)
                {
                    int hundredths=BitConverter.ToInt32(prop.Value,i*4);
                    if(hundredths>0) durations[i]=Math.Max(10,hundredths*10);
                }
        }
        catch { }
        return durations;
    }
    int CurrentDuration(bool moving,bool isLongIdle)
    {
        int[] values=moving?swingDurations:(isLongIdle?longDurations:idleDurations);
        int index=moving?swingFrame:(isLongIdle?longFrame:idleFrame);
        if(values==null || values.Length==0) return 100;
        return Math.Max(10,values[index%values.Length]);
    }

    static void FindHeadAnchors(Image image,FrameDimension dimension,int frames,out float[] xs,out float[] ys)
    {
        xs=new float[Math.Max(1,frames)];
        ys=new float[Math.Max(1,frames)];
        if(image==null) return;
        for(int frame=0;frame<xs.Length;frame++)
        {
            try
            {
                if(frames>1) image.SelectActiveFrame(dimension,frame);
                using(Bitmap b=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppArgb))
                {
                    using(Graphics g=Graphics.FromImage(b)) { g.Clear(Color.Transparent); g.DrawImage(image,0,0); }
                    int top=-1;
                    for(int y=0;y<b.Height && top<0;y++)
                        for(int x=0;x<b.Width;x++)
                            if(b.GetPixel(x,y).A>8) { top=y; break; }
                    if(top<0) continue;
                    int firstX=b.Width, lastX=-1;
                    for(int x=0;x<b.Width;x++)
                        if(b.GetPixel(x,top).A>8) { firstX=Math.Min(firstX,x); lastX=Math.Max(lastX,x); }
                    if(lastX>=0)
                    {
                        int target=(firstX+lastX)/2;
                        int anchorX=firstX;
                        for(int x=firstX;x<=lastX;x++)
                            if(b.GetPixel(x,top).A>8 && Math.Abs(x-target)<Math.Abs(anchorX-target)) anchorX=x;
                        xs[frame]=anchorX/(float)b.Width;
                        ys[frame]=top/(float)b.Height;
                    }
                }
            }
            catch { }
        }
    }

    void LoadConfig()
    {
        try
        {
            string t=File.ReadAllText(Path.Combine(root,"config.json"));
            int n; float f;
            bool versionedAnchor=System.Text.RegularExpressions.Regex.IsMatch(t,"\"anchor_mode\"\\s*:\\s*2");
            var m=System.Text.RegularExpressions.Regex.Match(t,"\"character_height\"\\s*:\\s*(\\d+)");
            if(m.Success && int.TryParse(m.Groups[1].Value,out n)) characterHeight=n;
            m=System.Text.RegularExpressions.Regex.Match(t,"\"rope_length\"\\s*:\\s*(\\d+)");
            if(m.Success && int.TryParse(m.Groups[1].Value,out n)) ropeLength=n;
            m=System.Text.RegularExpressions.Regex.Match(t,"\"character_rotation_scale\"\\s*:\\s*(-?[0-9.]+)");
            if(m.Success && float.TryParse(m.Groups[1].Value,out f)) characterRotationScale=f;
            m=System.Text.RegularExpressions.Regex.Match(t,"\"head_offset_x\"\\s*:\\s*(-?[0-9.]+)");
            if(m.Success && float.TryParse(m.Groups[1].Value,out f)) headOffsetX=f;
            m=System.Text.RegularExpressions.Regex.Match(t,"\"head_offset_y\"\\s*:\\s*(-?[0-9.]+)");
            if(m.Success && float.TryParse(m.Groups[1].Value,out f)) headOffsetY=f;
            m=System.Text.RegularExpressions.Regex.Match(t,"\"rope_color\"\\s*:\\s*\"([^\"]+)\"");
            if(m.Success) { try { ropeColor=ColorTranslator.FromHtml(m.Groups[1].Value); } catch { } }
            // Old versions measured offsets from the transparent canvas instead of the visible head.
            if(!versionedAnchor) { headOffsetX=0f; headOffsetY=0f; }
        }
        catch { }
    }

    public void SaveSettingsPublic(){SaveConfig();}
    void SaveConfig()
    {
        try
        {
            File.WriteAllText(Path.Combine(root,"config.json"),
                "{\n  \"character_height\": "+characterHeight+",\n  \"rope_length\": "+ropeLength+",\n  \"character_rotation_scale\": "+characterRotationScale.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\n  \"head_offset_x\": "+headOffsetX.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\n  \"head_offset_y\": "+headOffsetY.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\n  \"rope_color\": \""+ColorTranslator.ToHtml(ropeColor)+"\",\n  \"idle_gif\": \"assets/idle.gif\",\n  \"long_idle_gif\": \"assets/long_idle.gif\",\n  \"swing_gif\": \"assets/swing.gif\"\n}\n");
        }
        catch { }
        try
        {
            string configPath=Path.Combine(root,"config.json");
            string saved=File.ReadAllText(configPath);
            if(saved.IndexOf("\"anchor_mode\"",StringComparison.Ordinal)<0)
                File.WriteAllText(configPath,saved.Replace("\"character_height\"","\"anchor_mode\": 2,\n  \"character_height\""));
        }
        catch { }
    }

    void InitRope()
    {
        float len=Math.Max(1f,ropeLength);
        float seg=len/Segments;
        for(int i=0;i<=Segments;i++)
        {
            nx[i]=anchorX; ny[i]=anchorY+seg*i;
            px[i]=nx[i]; py[i]=ny[i];
        }
        ballX=nx[Segments]; ballY=ny[Segments];
    }

    void Constrain(int a,int b,float target,float stiffness)
    {
        float dx=nx[b]-nx[a], dy=ny[b]-ny[a];
        float dist=(float)Math.Sqrt(dx*dx+dy*dy);
        if(dist<0.0001f) return;
        float diff=(dist-target)/dist*stiffness;
        float mx=dx*diff, my=dy*diff;
        if(a==0) { nx[b]-=mx; ny[b]-=my; }
        else { nx[a]+=mx*0.5f; ny[a]+=my*0.5f; nx[b]-=mx*0.5f; ny[b]-=my*0.5f; }
    }

    void Tick(object sender,EventArgs e)
    {
        CURSORINFO ci=new CURSORINFO(); ci.cbSize=Marshal.SizeOf(typeof(CURSORINFO));
        bool shown=GetCursorInfo(ref ci);
        if(shown && (ci.flags & 1)!=0)
        {
            if(!Visible) Visible=true;
        }
        else
        {
            if(Visible) Visible=false;
            return;
        }
        Point now=Cursor.Position;
        float dx=now.X-lastMouseX;
        lastMouseX=now.X;
        cursorSpeed=cursorSpeed*0.82f+dx*0.18f;
        if(Math.Abs(dx)>1) lastMove=DateTime.UtcNow;
        Rectangle work=Screen.FromPoint(now).WorkingArea; int left=Math.Max(work.Left,Math.Min(work.Right-Width,now.X-250)); int top=Math.Max(work.Top,Math.Min(work.Bottom-Height,now.Y-60)); Location=new Point(left,top); anchorX=now.X-left; anchorY=now.Y-top;
        if(!paused)
        {
            float segLen=Math.Max(1f,ropeLength)/Segments;
            float drive=Math.Max(-5f,Math.Min(5f,cursorSpeed*0.16f));
            nx[0]=anchorX; ny[0]=anchorY;
            for(int i=1;i<=Segments;i++)
            {
                float vx=(nx[i]-px[i])*0.94f;
                float vy=(ny[i]-py[i])*0.94f;
                px[i]=nx[i]; py[i]=ny[i];
                nx[i]+=vx+drive*(i/(float)Segments);
                ny[i]+=vy+0.9f;
                if(i<=2) { nx[i]=anchorX+(nx[i]-anchorX)*0.55f; ny[i]=anchorY+(ny[i]-anchorY)*0.82f; }
            }
            for(int iter=0;iter<8;iter++)
            {
                nx[0]=anchorX; ny[0]=anchorY;
                for(int i=1;i<=Segments;i++) Constrain(i-1,i,segLen,0.88f);
            }
            ballX=nx[Segments]; ballY=ny[Segments];
        }
        bool movingNow=Math.Abs(cursorSpeed)>3f; bool longIdleNow=(DateTime.UtcNow-lastMove).TotalSeconds>5; if((DateTime.UtcNow-lastFrame).TotalMilliseconds>=CurrentDuration(movingNow,longIdleNow))
        {
            idleFrame=Next(idleFrame,idleFrames); longFrame=Next(longFrame,longFrames); swingFrame=Next(swingFrame,swingFrames);
            lastFrame=DateTime.UtcNow;
        }
        Render();
    }

    void Render()
    {
        if(!IsHandleCreated || Width<=0 || Height<=0) return;
        using(Bitmap bmp=new Bitmap(Width,Height,PixelFormat.Format32bppArgb))
        {
            using(Graphics g=Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                bool moving=Math.Abs(cursorSpeed)>3f;
                bool isLongIdle=(DateTime.UtcNow-lastMove).TotalSeconds>5;
                Image currentImage=idle; FrameDimension currentDim=idleDim; int currentFrame=idleFrame;
                float[] currentHeadX=idleHeadX, currentHeadY=idleHeadY;
                if(moving) { currentImage=swing; currentDim=swingDim; currentFrame=swingFrame; currentHeadX=swingHeadX; currentHeadY=swingHeadY; }
                else if(isLongIdle) { currentImage=longIdle; currentDim=longDim; currentFrame=longFrame; currentHeadX=longHeadX; currentHeadY=longHeadY; }
                currentImage.SelectActiveFrame(currentDim,currentFrame);
                int h=Math.Max(32,characterHeight);
                int w=(int)(h*currentImage.Width/(float)currentImage.Height);
                int anchorFrame=currentFrame%currentHeadX.Length;
                float imageAnchorX=currentHeadX[anchorFrame]*w;
                float imageAnchorY=currentHeadY[anchorFrame]*h;
                PointF[] points=new PointF[Segments+1];
                for(int i=0;i<=Segments;i++) points[i]=new PointF(nx[i],ny[i]);
                float endDx=ballX-nx[Segments-1], endDy=ballY-ny[Segments-1];
                float endLength=(float)Math.Sqrt(endDx*endDx+endDy*endDy);
                if(endLength>0.01f) points[Segments]=new PointF(ballX+endDx/endLength*2f,ballY+endDy/endLength*2f);
                using(Pen pen=new Pen(ropeColor,2f)) g.DrawLines(pen,points);
                float angle=(float)(Math.Atan2(ballX-anchorX,Math.Max(1f,ballY-anchorY))*180d/Math.PI);
                g.TranslateTransform(ballX,ballY);
                g.RotateTransform(angle*characterRotationScale);
                g.DrawImage(currentImage,new RectangleF(-imageAnchorX-headOffsetX,-imageAnchorY-headOffsetY,w,h));
                g.ResetTransform();
            }
            Upload(bmp);
        }
    }

    void Upload(Bitmap bmp)
    {
        IntPtr screen=GetDC(IntPtr.Zero), mem=CreateCompatibleDC(screen), hb=bmp.GetHbitmap(Color.FromArgb(0)), old=SelectObject(mem,hb);
        try
        {
            POINT dst=new POINT { X=Left, Y=Top };
            POINT src=new POINT { X=0, Y=0 };
            SIZE size=new SIZE { CX=bmp.Width, CY=bmp.Height };
            BLENDFUNCTION blend=new BLENDFUNCTION { BlendOp=AC_SRC_OVER, BlendFlags=0, SourceConstantAlpha=255, AlphaFormat=AC_SRC_ALPHA };
            UpdateLayeredWindow(Handle,screen,ref dst,ref size,mem,ref src,0,ref blend,ULW_ALPHA);
        }
        finally
        {
            if(old!=IntPtr.Zero) SelectObject(mem,old);
            if(hb!=IntPtr.Zero) DeleteObject(hb);
            if(mem!=IntPtr.Zero) DeleteDC(mem);
            if(screen!=IntPtr.Zero) ReleaseDC(IntPtr.Zero,screen);
        }
    }
}

internal sealed class SettingsForm : Form
{
    public SettingsForm(AlphaPendant p)
    {
        Text="Cursor Pendant Settings"; FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false; StartPosition=FormStartPosition.CenterScreen; ClientSize=new Size(390,380);
        Label sizeLabel=new Label(); sizeLabel.Text="Character size"; sizeLabel.SetBounds(20,16,150,20); Controls.Add(sizeLabel);
        Label sizeValue=new Label(); sizeValue.Text=p.CharacterHeight+" px"; sizeValue.SetBounds(310,16,60,20); Controls.Add(sizeValue);
        TrackBar sizeBar=new TrackBar(); sizeBar.Minimum=32; sizeBar.Maximum=400; sizeBar.Value=p.CharacterHeight; sizeBar.SetBounds(20,38,350,42); sizeBar.ValueChanged+=delegate { sizeValue.Text=sizeBar.Value+" px"; p.CharacterHeight=sizeBar.Value; }; Controls.Add(sizeBar);
        Label ropeLabel=new Label(); ropeLabel.Text="Rope length"; ropeLabel.SetBounds(20,90,150,20); Controls.Add(ropeLabel);
        Label ropeValue=new Label(); ropeValue.Text=p.RopeLength+" px"; ropeValue.SetBounds(310,90,60,20); Controls.Add(ropeValue);
        TrackBar ropeBar=new TrackBar(); ropeBar.Minimum=0; ropeBar.Maximum=480; ropeBar.Value=p.RopeLength; ropeBar.SetBounds(20,112,350,42); ropeBar.ValueChanged+=delegate { ropeValue.Text=ropeBar.Value+" px"; p.RopeLength=ropeBar.Value; }; Controls.Add(ropeBar);
        Label xLabel=new Label(); xLabel.Text="Horizontal offset"; xLabel.SetBounds(20,165,180,20); Controls.Add(xLabel); Label xValue=new Label(); xValue.Text=p.HeadOffsetX+" px"; xValue.SetBounds(310,165,60,20); Controls.Add(xValue); TrackBar xBar=new TrackBar(); xBar.Minimum=-200; xBar.Maximum=200; xBar.Value=Math.Max(-200,Math.Min(200,(int)p.HeadOffsetX)); xBar.SetBounds(20,187,350,42); xBar.ValueChanged+=delegate { xValue.Text=xBar.Value+" px"; p.HeadOffsetX=xBar.Value; }; Controls.Add(xBar);
        Label yLabel=new Label(); yLabel.Text="Vertical offset"; yLabel.SetBounds(20,240,180,20); Controls.Add(yLabel); Label yValue=new Label(); yValue.Text=p.HeadOffsetY+" px"; yValue.SetBounds(310,240,60,20); Controls.Add(yValue); TrackBar yBar=new TrackBar(); yBar.Minimum=-200; yBar.Maximum=200; yBar.Value=Math.Max(-200,Math.Min(200,(int)p.HeadOffsetY)); yBar.SetBounds(20,262,350,42); yBar.ValueChanged+=delegate { yValue.Text=yBar.Value+" px"; p.HeadOffsetY=yBar.Value; }; Controls.Add(yBar);
        Button colorButton=new Button(); colorButton.Text="Rope color"; colorButton.SetBounds(20,335,110,28); colorButton.Click+=delegate { ColorDialog d=new ColorDialog(); d.Color=p.RopeColor; if(d.ShowDialog()==DialogResult.OK) p.RopeColor=d.Color; }; Controls.Add(colorButton);
        FormClosed+=delegate { p.SaveSettingsPublic(); }; Button close=new Button(); close.Text="Close"; close.SetBounds(290,335,80,28); close.Click+=delegate { Close(); }; Controls.Add(close);
    }
}

internal sealed class Host : Form
{
    public Host()
    {
        ShowInTaskbar=false; WindowState=FormWindowState.Minimized; Opacity=0;
        AlphaPendant p=new AlphaPendant();
        NotifyIcon t=new NotifyIcon(); t.Text="Cursor Pendant"; t.Icon=SystemIcons.Information;
        ContextMenuStrip m=new ContextMenuStrip();
        m.Items.Add("Show / Hide",null,delegate { p.Visible=!p.Visible; });
        m.Items.Add("Pause / Resume",null,delegate { p.TogglePause(); });
        m.Items.Add("Settings",null,delegate { SettingsForm f=new SettingsForm(p); f.Show(); f.Activate(); });
        ToolStripMenuItem startupItem=new ToolStripMenuItem("Start with Windows");
        startupItem.CheckOnClick=true;
        startupItem.Checked=IsStartupEnabled();
        startupItem.Click+=delegate { SetStartup(startupItem.Checked); };
        m.Items.Add(startupItem);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Exit",null,delegate { t.Visible=false; Application.Exit(); });
        t.ContextMenuStrip=m; t.Visible=true;
        Shown+=delegate { Hide(); p.Show(); p.BringToFront(); };
    }

    const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "CursorPendant";

    static bool IsStartupEnabled()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunPath, false))
            return key != null && key.GetValue(RunName) != null;
    }

    static void SetStartup(bool enabled)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunPath))
        {
            if (enabled)
                key.SetValue(RunName, "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CursorPendant.exe") + "\"");
            else
                key.DeleteValue(RunName, false);
        }
    }
}
internal static class Program
{
    [STAThread] static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new Host()); }
}

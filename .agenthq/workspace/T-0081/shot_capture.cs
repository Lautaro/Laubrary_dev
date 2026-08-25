// Captures the window whose OS title contains TAG via PrintWindow(PW_RENDERFULLCONTENT).
// Roslyn-scripting friendly: no `using`, no attributes, no P/Invoke declarations — everything
// goes through GetDelegateForFunctionPointer on user32 entry points resolved by LoadLibrary/GetProcAddress
// is not available either, so we use System.Windows.Forms-free reflection over a tiny dynamic assembly.

string TAG = "%TAG%";
string OUT = @"%OUT%";

var asmName = new System.Reflection.AssemblyName("PWShot" + System.Guid.NewGuid().ToString("N"));
var asmB = System.AppDomain.CurrentDomain.DefineDynamicAssembly(asmName, System.Reflection.Emit.AssemblyBuilderAccess.Run);
var modB = asmB.DefineDynamicModule("m");
var tb = modB.DefineType("PW", System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);

System.Action<string, string, System.Type, System.Type[], System.Runtime.InteropServices.CharSet> pinvoke =
    (name, dll, ret, args, cs) =>
    {
        var mb = tb.DefinePInvokeMethod(name, dll,
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static
                | System.Reflection.MethodAttributes.PinvokeImpl,
            System.Reflection.CallingConventions.Standard, ret, args,
            System.Runtime.InteropServices.CallingConvention.Winapi, cs);
        mb.SetImplementationFlags(mb.GetMethodImplementationFlags() | System.Reflection.MethodImplAttributes.PreserveSig);
    };

// RECT as a nested struct
var rectB = modB.DefineType("RECTPW", System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.SequentialLayout
    | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Serializable, typeof(System.ValueType));
rectB.DefineField("L", typeof(int), System.Reflection.FieldAttributes.Public);
rectB.DefineField("T", typeof(int), System.Reflection.FieldAttributes.Public);
rectB.DefineField("R", typeof(int), System.Reflection.FieldAttributes.Public);
rectB.DefineField("B", typeof(int), System.Reflection.FieldAttributes.Public);
var rectType = rectB.CreateType();

pinvoke("IsWindowVisible", "user32.dll", typeof(bool), new System.Type[] { typeof(System.IntPtr) },
    System.Runtime.InteropServices.CharSet.Auto);
pinvoke("GetWindowTextW", "user32.dll", typeof(int),
    new System.Type[] { typeof(System.IntPtr), typeof(System.Text.StringBuilder), typeof(int) },
    System.Runtime.InteropServices.CharSet.Unicode);
pinvoke("GetWindowRect", "user32.dll", typeof(bool),
    new System.Type[] { typeof(System.IntPtr), rectType.MakeByRefType() },
    System.Runtime.InteropServices.CharSet.Auto);
pinvoke("PrintWindow", "user32.dll", typeof(bool),
    new System.Type[] { typeof(System.IntPtr), typeof(System.IntPtr), typeof(uint) },
    System.Runtime.InteropServices.CharSet.Auto);
pinvoke("GetDesktopWindow", "user32.dll", typeof(System.IntPtr), System.Type.EmptyTypes,
    System.Runtime.InteropServices.CharSet.Auto);
pinvoke("GetWindow", "user32.dll", typeof(System.IntPtr),
    new System.Type[] { typeof(System.IntPtr), typeof(uint) }, System.Runtime.InteropServices.CharSet.Auto);

var pw = tb.CreateType();
var mIsVis = pw.GetMethod("IsWindowVisible");
var mText = pw.GetMethod("GetWindowTextW");
var mRect = pw.GetMethod("GetWindowRect");
var mPrint = pw.GetMethod("PrintWindow");
var mDesktop = pw.GetMethod("GetDesktopWindow");
var mGetWindow = pw.GetMethod("GetWindow");

// walk top-level windows: GetWindow(desktop, GW_CHILD=5) then GW_HWNDNEXT=2
var hwnd = (System.IntPtr)mGetWindow.Invoke(null, new object[] { (System.IntPtr)mDesktop.Invoke(null, null), (uint)5 });
System.IntPtr found = System.IntPtr.Zero;
int guard = 0;
while (hwnd != System.IntPtr.Zero && guard++ < 20000)
{
    if ((bool)mIsVis.Invoke(null, new object[] { hwnd }))
    {
        var sb = new System.Text.StringBuilder(512);
        mText.Invoke(null, new object[] { hwnd, sb, 512 });
        if (sb.ToString().Contains(TAG)) { found = hwnd; break; }
    }
    hwnd = (System.IntPtr)mGetWindow.Invoke(null, new object[] { hwnd, (uint)2 });
}
if (found == System.IntPtr.Zero) return "HWND containing '" + TAG + "' not found";

var rargs = new object[] { found, System.Activator.CreateInstance(rectType) };
mRect.Invoke(null, rargs);
var r = rargs[1];
int L = (int)rectType.GetField("L").GetValue(r);
int T = (int)rectType.GetField("T").GetValue(r);
int R = (int)rectType.GetField("R").GetValue(r);
int B = (int)rectType.GetField("B").GetValue(r);
int w = R - L, h = B - T;
if (w <= 0 || h <= 0) return "bad rect " + w + "x" + h;

var drawing = System.Reflection.Assembly.Load("System.Drawing");
var bmpType = drawing.GetType("System.Drawing.Bitmap");
var gType = drawing.GetType("System.Drawing.Graphics");
var bmp = System.Activator.CreateInstance(bmpType, new object[] { w, h });
var g = gType.GetMethod("FromImage").Invoke(null, new object[] { bmp });
var hdc = (System.IntPtr)gType.GetMethod("GetHdc").Invoke(g, null);
mPrint.Invoke(null, new object[] { found, hdc, (uint)2 });
gType.GetMethod("ReleaseHdc", System.Type.EmptyTypes).Invoke(g, null);
var fmtType = drawing.GetType("System.Drawing.Imaging.ImageFormat");
var png = fmtType.GetProperty("Png").GetValue(null, null);
bmpType.GetMethod("Save", new System.Type[] { typeof(string), fmtType }).Invoke(bmp, new object[] { OUT, png });
return "saved " + OUT + " (" + w + "x" + h + ")";

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Doorstop
{
    public static class Entrypoint
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Callback();
        private static readonly Callback Poll = WaitForUnity;
        private static int attempts;
        public static readonly string Root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern void digfloor_schedule(Callback callback, double delay);
        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern int digfloor_is_native();

        private const string BuildVersion = "0.2.0";
        public static void Start()
        {
            try
            {
                File.WriteAllText(Path.Combine(Root, "ValheimBridge.log"), DateTime.Now.ToString("s") + " Starting ValheimBridge " + BuildVersion + "\n");
                if (digfloor_is_native() != 1)
                    throw new PlatformNotSupportedException("ValheimBridge requires native ARM64; Rosetta is not supported.");
                Log("Native ARM64 confirmed; waiting for Unity's main-thread context.");
                digfloor_schedule(Poll, 0.25);
            }
            catch (Exception ex) { Log(ex.ToString()); }
        }

        private static void WaitForUnity()
        {
            try
            {
                SynchronizationContext context = SynchronizationContext.Current;
                if (context != null && context.GetType().FullName == "UnityEngine.UnitySynchronizationContext")
                {
                    Log("Unity main-thread context ready; scheduling component.");
                    context.Post(_ => ValheimBridge.Bridge.Create(), null);
                }
                else if (++attempts < 240)
                    digfloor_schedule(Poll, 0.25);
                else
                    Log("Timed out waiting for Unity; bridge was not started.");
            }
            catch (Exception ex) { Log(ex.ToString()); }
        }

        public static void Log(string message)
        {
            try { File.AppendAllText(Path.Combine(Root, "ValheimBridge.log"), DateTime.Now.ToString("s") + " " + message + "\n"); }
            catch { /* Logging must never interrupt the game. */ }
        }
    }
}

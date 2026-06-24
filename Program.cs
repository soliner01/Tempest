using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA2;
using FlaUI.UIA3;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.ApplicationServices;

namespace Tempest
{
    class Program
    {
        [DllImport("User32.dll")]
        public static extern short GetAsyncKeyState(Keys keys);

        static void Main(string[] args)
        {
            Console.WriteLine("Tempest");
            Console.WriteLine();
            //The below 2 lines are solely for debugging purposes, it launches FlaUInspect and immediately returns to end the program.
            //FlaUI.Core.Application.Launch("flauinspect");
            //return;
            string selfName = Methods.DetectForeground();
            string logPath = selfName;
            for (int i=0; i<4; i++)
            {
                logPath = Path.GetDirectoryName(logPath)!;
            }
            string logPathReadable = logPath + "\\Logs\\AutomationLogReadable.txt";
            string logPathNormal = logPath + "\\Logs\\AutomationLog.txt";
            string logPathElements = logPath + "\\Logs\\ElementLog.txt";
            if (File.Exists(logPathReadable) || File.Exists(logPathNormal) || File.Exists(logPathElements))
            {
                throw new FileLoadException("Error: A log file with the name AutomationLogReadable.txt, AutomationLog.txt, or ElementLog.txt exists in the Logs folder. Please remove any such files from the Logs folder.");
            }
            Console.WriteLine("Enter the program pathname:");
            string appName = Console.ReadLine() ?? throw new FileNotFoundException("Cannot take an empty string as the path.");
            Console.WriteLine("Enter the maximum number of interactions (enter 0 for int.MaxValue):");
            int counter = Convert.ToInt32(Console.ReadLine());
            if (counter < 0)
            {
                throw new InvalidCastException("Cannot take a negative number as input. The input must be either 0 or a positive integer.");
            }
            if (counter == 0)
            {
                counter = int.MaxValue;
            }
            var app = FlaUI.Core.Application.Launch(appName);
            Thread.Sleep(20000); //Unfortunately, this is a required sleep to make this work as some applications just start a completely separate process and then kill the current process.
            string targetName = Methods.DetectForeground();
            var processList = Process.GetProcesses().Where(p => p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle == targetName).ToArray();
            Process newestProcess = null!;
            foreach (Process p in processList) //Determine which process is newest and is the correct program, and attach that process.
            {
                if (p.ProcessName != "ApplicationFrameHost")
                {
                    if (newestProcess == null)
                        newestProcess = p;
                    else if ((int)(p.StartTime - new DateTime(1970, 1, 1)).TotalSeconds > (int)(newestProcess.StartTime - new DateTime(1970, 1, 1)).TotalSeconds)
                        newestProcess = p;
                }
            }
            app = FlaUI.Core.Application.Attach(newestProcess); //Redefine app to the correct process, in case the process was terminated in cases such as Microsoft Edge.
            var mainWindow = app.GetMainWindow(new UIA2Automation());
            using (StreamWriter writer = new StreamWriter(logPathReadable))
            {
                writer.WriteLine(DateTime.Now + ": Opened app " + app.Name + ".");
            }
            using (StreamWriter writer = new StreamWriter(logPathNormal))
            {
                writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Opened,,"+ app.Name.Replace(",", ""));
            }
            using (StreamWriter writer = new StreamWriter(logPathElements))
            {
                writer.Write("");
            }
            Wait.UntilResponsive(mainWindow);
            ConditionFactory cf = new ConditionFactory(new UIA2PropertyLibrary());
            string targetProcess = app.Name; //The name of the target process, which will be used for the purpose of checking if the process has been closed later on in the program.
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;
            bool failsafe = false;
            Parallel.Invoke(
                () =>
                {
                    try
                    {
                        string frontName = ""; //This is to get the name of the frontmost app, which will be used when writing some things to the log file.
                        bool run = true;
                        int refocusCounter = 0;
                        while (run == true)
                        {
                            counter--; //Counts down the number of remaining interactions left based on the user's input earlier in the program.
                            if (counter == 0) //If the counter hits 0, this is the final iteration.
                            {
                                run = false;
                            }
                            if (Methods.DetectForeground() == selfName) //If the automation program itself is detected to be the frontmost window, the program will immediately terminate automation so it can't close itself.
                            {
                                Console.WriteLine("Detected that the program is about to close itself as part of its automation. Activating automatic failsafe...");
                                using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                                {
                                    writer.WriteLine(DateTime.Now + ": Error: Detected that the program is about to close itself as part of its automation. Activating automatic failsafe...");
                                }
                                using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                                {
                                    writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Automation Self-Termination Error," + mainWindow.Name.Replace(",", "") + "," + app.Name.Replace(",", ""));
                                }
                                failsafe = true;
                                FlaUI.Core.Input.Keyboard.Type(VirtualKeyShort.ESC);
                                Thread.Yield();
                                token.ThrowIfCancellationRequested();
                            }
                            frontName = app.Name;
                            Methods.Interact(mainWindow, cf, app, logPathReadable, logPathNormal, logPathElements, 0); //Interact with a random element of interest.
                            if (token.IsCancellationRequested) //If the failsafe has been activated, throw an error so the program stops automation.
                            {
                                token.ThrowIfCancellationRequested();
                            }
                            if (Methods.DetectForeground() != targetName && app.Name != targetProcess) //If the window and app are not the desired window and app (window is tacked on as a bit of extra leniency), increase the counter.
                            {
                                refocusCounter++;
                                if (refocusCounter >= 20) //If the threshold is hit, close the frontmost app unless it would self-terminate automation.
                                {
                                    using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                                    {
                                        writer.WriteLine(DateTime.Now + ": Refocus counter has hit the designated threshold. Closing the frontmost app unless it would self-terminate automation.");
                                    }
                                    using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                                    {
                                        writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Refocusing,,");
                                    }
                                    if (Methods.DetectForeground() != selfName)
                                    {
                                        app.Close();
                                        Thread.Sleep(5000); //Required sleep in case the program needs to be killed for failing to close.
                                    }
                                }
                            }
                            else //If the window is the desired window, we reset the counter.
                            {
                                refocusCounter = 0;
                            }
                            if (app.HasExited) //If the app of focus has exited, determine if ANY instance of the program that was launched at the start of the program is still running.
                            {
                                using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                                {
                                    writer.WriteLine(DateTime.Now + ": " + frontName + " closed.");
                                }
                                using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                                {
                                    writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Closed,," + frontName.Replace(",", ""));
                                }
                                processList = null;
                                processList = Process.GetProcesses().Where(p => p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle == targetName).ToArray();
                                bool isTargetRunning = false;
                                foreach (Process process in processList) //Determines if the target process is still running.
                                {
                                    if (process.ProcessName == targetProcess)
                                    {
                                        isTargetRunning = true;
                                    }
                                }
                                if (!isTargetRunning) //If the app launched at the start of the program is no longer running, launch it again so we can attach it in the code below (a copy-paste from some of the beginning code in main).
                                {
                                    app = FlaUI.Core.Application.Launch(appName);
                                    using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                                    {
                                        writer.WriteLine(DateTime.Now + ": Re-opened app " + app.Name + ".");
                                    }
                                    using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                                    {
                                        writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Opened,," + app.Name.Replace(",", ""));
                                    }
                                    Thread.Sleep(20000); //Unfortunately, this is a required sleep to make this work as some applications just start a completely separate process and then kill the current process.
                                }
                                targetName = Methods.DetectForeground(); //Regardless of if the original app is running or not, attach the frontmost app as it is our new focus. (a copy-paste from some of the beginning code in main).
                                processList = Process.GetProcesses().Where(p => p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle == targetName).ToArray();
                                newestProcess = null!;
                                foreach (Process p in processList) //Determine which process is newest and is the correct program, and attach that process.
                                {
                                    if (p.ProcessName != "ApplicationFrameHost")
                                    {
                                        if (newestProcess == null)
                                            newestProcess = p;
                                        else if ((int)(p.StartTime - new DateTime(1970, 1, 1)).TotalSeconds > (int)(newestProcess.StartTime - new DateTime(1970, 1, 1)).TotalSeconds)
                                            newestProcess = p;
                                    }
                                }
                                app = FlaUI.Core.Application.Attach(newestProcess); //Redefine app to the correct process, in case the process was terminated in cases such as Microsoft Edge.
                                mainWindow = app.GetMainWindow(new UIA2Automation());
                                using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                                {
                                    writer.WriteLine(DateTime.Now + ": Switched focus to window " + mainWindow.Name + " in app " + app.Name + ".");
                                }
                                using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                                {
                                    writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Switched Focus," + mainWindow.Name.Replace(",", "") + "," + app.Name.Replace(",", ""));
                                }
                                Wait.UntilResponsive(mainWindow);
                            }
                            else
                            {
                                var tempApp = Methods.MainWindowHandler(); //Checks if we need to redefine the main window, since in certain circumstances redefining the window would cause problems due to having no selectable items for whatever reason.
                                if (tempApp != null) //If we deem it safe to redefine the main window, we do so.
                                {
                                    app = tempApp;
                                    mainWindow = app.GetMainWindow(new UIA2Automation());
                                    using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                                    {
                                        writer.WriteLine(DateTime.Now + ": Switched focus to window " + mainWindow.Name + " in app " + app.Name + ".");
                                    }
                                    using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                                    {
                                        writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Switched Focus," + mainWindow.Name.Replace(",", "") + "," + app.Name.Replace(",", ""));
                                    }
                                    Wait.UntilResponsive(mainWindow);
                                }
                            }
                        }
                        cts.Cancel();
                        Thread.Yield();
                        cts.Dispose();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                        Console.WriteLine("Failsafe activation detected. Terminating automation...");
                        cts.Dispose();
                        failsafe = true;
                        using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                        {
                            writer.WriteLine(DateTime.Now + ": Caught an error. Details:");
                            writer.WriteLine(e);
                            writer.WriteLine(DateTime.Now + ": Automation terminated.");
                        }
                        using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                        {
                            writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Automation terminated,,");
                        }
                    }
                },
                () =>
                {
                    FailsafeCheck(cts, token, logPathReadable, logPathNormal);
                }
            );
            if (!failsafe)
            {
                Console.WriteLine("Automation complete.");
                using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                {
                    writer.WriteLine(DateTime.Now + ": Automation complete.");
                }
                using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                {
                    writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Automation complete,,");
                }
                if (Methods.DetectForeground() != selfName)
                {
                    app.Close();
                }
            }
        }

        public static void FailsafeCheck(CancellationTokenSource cts, CancellationToken token, string logPathReadable, string logPathNormal)
        {
            while (!cts.IsCancellationRequested)
            {
                if ((GetAsyncKeyState(System.Windows.Forms.Keys.Escape) & 0x8000) > 0)
                {
                    Console.WriteLine("Failsafe activated.");
                    using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                    {
                        writer.WriteLine(DateTime.Now + ": Failsafe activated.");
                    }
                    using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                    {
                        writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Failsafe activated,,");
                    }
                    cts.Cancel();
                    return;
                }
            }
        }
    }

    public class Methods
    {
        [DllImport("user32")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        public static extern bool SendMessage(IntPtr hWnd, uint Msg, int wParam, StringBuilder lParam);

        const int WM_GETTEXT = 0x000D;


        public static string DetectForeground()
        {
            string previous_title = null!;

            for (; ; )
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) continue;

                StringBuilder sb = new StringBuilder(700);

                SendMessage(hwnd, WM_GETTEXT, sb.Capacity, sb);

                string title = sb.ToString();

                if (previous_title != title)
                {
                    previous_title = title;

                    //Console.WriteLine($"Window title: {title}");
                    return title;
                }

                Thread.Sleep(100);
            }
        }


        private static Random rng = new Random();

        public static void Interact(Window mainWindow, ConditionFactory cf, FlaUI.Core.Application app, string logPathReadable, string logPathNormal, string logPathElements, int attempt)
        {
            AutomationElement[] elementsOnScreen = null!;
            if (mainWindow.ModalWindows.Length > 0)
            {
                var modalWindows = mainWindow.ModalWindows;
                try
                {
                    elementsOnScreen = modalWindows[^1]
                        .FindAllDescendants().Where(e =>
                        (e.Patterns.ExpandCollapse.IsSupported || e.Patterns.Invoke.IsSupported || e.Patterns.SelectionItem.IsSupported || e.Patterns.Text.IsSupported || e.Patterns.Toggle.IsSupported)
                        && e.Properties.Name.IsSupported && !(e.Name.ToString().Equals("Minimize") || e.Name.ToString().Equals("System")))
                        .ToArray(); //Gets an array of all elements of interest currently on screen.
                }
                catch (Exception)
                {
                    if (attempt == 100)
                    {
                        Console.WriteLine("Repeatedly encountered problematic elements. Continuing automation may not be possible. Activating automatic failsafe...");
                        using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                        {
                            writer.WriteLine(DateTime.Now + ": Error: Repeatedly encountered problematic elements. Continuing automation may not be possible. Activating automatic failsafe...");
                        }
                        using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                        {
                            writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Repeated Problematic Elements Error," + mainWindow.Name.Replace(",", "") + "," + app.Name.Replace(",", ""));
                        }
                        FlaUI.Core.Input.Keyboard.Type(VirtualKeyShort.ESC);
                        return;
                    }
                    Console.WriteLine("There was a problematic element. Retrying...");
                    Interact(mainWindow, cf, app, logPathReadable, logPathNormal, logPathElements, attempt++);
                    return;
                }
                using (StreamWriter writer = new StreamWriter(logPathElements, append: true))
                {
                    writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ":");
                }
                int counter = 0; //Counter to cap the number of elements written at 100 for efficiency purposes.
                foreach (var e in elementsOnScreen)
                {
                    try
                    {
                        using (StreamWriter writer = new StreamWriter(logPathElements, append: true))
                        {
                            if (counter <= 100) //If 100 elements from this window have already been written to the ElementLog file, the remaining elements will simply be skipped to ensure the program isn't stuck writing for too long.
                            {
                                writer.WriteLine(e);
                                counter++;
                            }
                        }
                    }
                    catch (Exception) { }
                }
            }
            else
            {
                try
                {
                    elementsOnScreen = mainWindow.FindAllDescendants().Where(e =>
                        (e.Patterns.ExpandCollapse.IsSupported || e.Patterns.Invoke.IsSupported || e.Patterns.SelectionItem.IsSupported || e.Patterns.Text.IsSupported || e.Patterns.Toggle.IsSupported)
                        && e.Properties.Name.IsSupported && !(e.Name.ToString().Equals("Minimize") || e.Name.ToString().Equals("System")))
                        .ToArray(); //Gets an array of all elements of interest currently on screen.
                }
                catch (Exception)
                {
                    Console.WriteLine("Encountered a problematic element. Trying again.");
                    if (attempt == 100)
                    {
                        Console.WriteLine("Repeatedly encountered problematic elements. Continuing automation may not be possible. Activating automatic failsafe...");
                        using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                        {
                            writer.WriteLine(DateTime.Now + ": Error: Repeatedly encountered problematic elements. Continuing automation may not be possible. Activating automatic failsafe...");
                        }
                        using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                        {
                            writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Repeated Problematic Elements Error," + mainWindow.Name.Replace(",", "") + "," + app.Name.Replace(",", ""));
                        }
                        FlaUI.Core.Input.Keyboard.Type(VirtualKeyShort.ESC);
                        return;
                    }
                    Interact(mainWindow, cf, app, logPathReadable, logPathNormal, logPathElements, attempt++);
                    return;
                }
                using (StreamWriter writer = new StreamWriter(logPathElements, append: true))
                {
                    writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ":");
                }
                foreach (var e in elementsOnScreen)
                {
                    try
                    {
                        using (StreamWriter writer = new StreamWriter(logPathElements, append: true))
                        {
                            writer.WriteLine(e);
                        }
                    }
                    catch (Exception) { }
                }
            }
            int index = rng.Next(0, elementsOnScreen.Length); //Randomly selects an element of interest to interact with
            var item = elementsOnScreen[index];
            try
            {
                bool repeat = true;
                while (repeat)
                {
                    if (item.Properties.IsOffscreen)
                    {
                        index = rng.Next(0, elementsOnScreen.Length); //Randomly selects an element of interest to interact with
                        item = elementsOnScreen[index];
                    }
                    else
                    {
                        using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                        {
                            writer.WriteLine(DateTime.Now + ": Clicked on element " + item.Name + " in window " + mainWindow.Name + " in app " + app.Name + ".");
                        }
                        using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                        {
                            writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Clicked " + item.Name.Replace(",", "") + "," + mainWindow.Name.Replace(",", "") + "," + app.Name.Replace(",", ""));
                        }
                        item.Click();
                        repeat = false;
                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine("An exception occurred: Failed to click on item. Item information: " + item);
                using (StreamWriter writer = new StreamWriter(logPathReadable, append: true))
                {
                    writer.WriteLine(DateTime.Now + ": An exception occurred: Failed to click on item " + item.Name + " in window " + mainWindow.Name + " in app " + app.Name + ".");
                }
                using (StreamWriter writer = new StreamWriter(logPathNormal, append: true))
                {
                    writer.WriteLine(DateTimeOffset.Now.ToUnixTimeMilliseconds() + ",Failed Click " + item.Name.Replace(",", "") + "," + mainWindow.Name.Replace(",", "") + "," + app.Name.Replace(",", ""));
                }
            }
            Thread.Sleep(1500);
            return;
        }

        public static FlaUI.Core.Application MainWindowHandler()
        {
            string targetName = Methods.DetectForeground(); //Start of check to determine if the foreground window has changed (a copy-paste from some of the beginning code in main, with some additions specifically made for this check
            var processList = Process.GetProcesses().Where(p => p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle == targetName).ToArray();
            Process newestProcess = null!;
            foreach (Process p in processList) //Determine which process is newest and is the correct program.
            {
                if (p.ProcessName != "ApplicationFrameHost")
                {
                    if (newestProcess == null)
                        newestProcess = p;
                    else if ((int)(p.StartTime - new DateTime(1970, 1, 1)).TotalSeconds > (int)(newestProcess.StartTime - new DateTime(1970, 1, 1)).TotalSeconds)
                        newestProcess = p;
                }
            }
            if (newestProcess is null)
            {
                return null!;
            }
            var app = FlaUI.Core.Application.Attach(newestProcess); //Redefine app to the process that was just detected to be the frontmost window, as it can be assumed that a new process has just opened and should be the new focus.
            var mainWindow = app.GetMainWindow(new UIA2Automation());
            Wait.UntilResponsive(mainWindow);
            var elementsOnScreen = mainWindow.FindAllDescendants().Where(e =>
                (e.Patterns.ExpandCollapse.IsSupported || e.Patterns.Invoke.IsSupported || e.Patterns.SelectionItem.IsSupported || e.Patterns.Text.IsSupported || e.Patterns.Toggle.IsSupported)
                && e.Properties.Name.IsSupported && !(e.Name.ToString().Equals("Minimize") || e.Name.ToString().Equals("Maximize") || e.Name.ToString().Equals("Restore") || e.Name.ToString().Equals("System")))
                .ToArray(); //Gets an array of all elements of interest currently on screen.
            if(elementsOnScreen.Length == 0)
            {
                return null!;
            }
            return app;
        }
    }
}
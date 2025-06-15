using System;
using System.Windows.Forms;
using DotNetPlugin.NativeBindings.SDK;
using x64DbgMCPServer.Properties;

namespace DotNetPlugin
{
    partial class Plugin
    {
        protected override void SetupMenu(Menus menus)
        {
            menus.Main
                .AddAndConfigureItem("&StartMCPServer", StartMCPServer).SetIcon(Resources.AboutIcon).Parent
                .AddAndConfigureItem("&StopMCPServer", StopMCPServer).SetIcon(Resources.AboutIcon).Parent
                .AddAndConfigureItem("&About...", OnAboutMenuItem).SetIcon(Resources.AboutIcon);
            //.AddAndConfigureItem("&CustomCommand", ExecuteCustomCommand).SetIcon(Resources.AboutIcon).Parent
            //.AddAndConfigureItem("&DotNetDumpProcess", OnDumpMenuItem).SetHotKey("CTRL+F12").Parent
            //.AddAndConfigureSubMenu("sub menu")
            //    .AddItem("sub menu entry1", menuItem => Console.WriteLine($"hEntry={menuItem.Id}"))
            //    .AddSeparator()
            //    .AddItem("sub menu entry2", menuItem => Console.WriteLine($"hEntry={menuItem.Id}"));
        }

        public void OnAboutMenuItem(MenuItem menuItem)
        {
            MessageBox.Show(HostWindow, "x64DbgMCPServer Plugin For x64dbg\nCoded By AgentSmithers", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void OnDumpMenuItem(MenuItem menuItem)
        {
            if (!Bridge.DbgIsDebugging())
            {
                Console.WriteLine("You need to be debugging to use this Command");
                return;
            }
            Bridge.DbgCmdExec("DotNetDumpProcess");
        }

        public static void ExecuteCustomCommand(MenuItem menuItem)
        {
            if (!Bridge.DbgIsDebugging())
            {
                Console.WriteLine("You need to be debugging to use this Command");
                return;
            }
            Bridge.DbgCmdExec("DumpModuleToFile");
        }
        public static void StartMCPServer(MenuItem menuItem)
        {
            Bridge.DbgCmdExec("StartMCPServer");
        }
        public static void StopMCPServer(MenuItem menuItem)
        {
            Bridge.DbgCmdExec("StopMCPServer");
        }
    }
}

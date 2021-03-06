﻿#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UlteriusServer.TaskServer.Api.Serialization;
using vtortola.WebSockets;

#endregion

namespace UlteriusServer.TaskServer.Api.Controllers.Impl
{
    public class WindowsController : ApiController
    {
        private readonly WebSocket client;
        private readonly Packets packet;
        private readonly ApiSerializator serializator = new ApiSerializator();

        public WindowsController(WebSocket client, Packets packet)
        {
            this.client = client;
            this.packet = packet;
        }

        private bool AllOneColor(Bitmap bmp)
        {
            var bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly,
                PixelFormat.Format8bppIndexed);
            var rgbValues = new byte[bmpData.Stride*bmpData.Height];
            Marshal.Copy(bmpData.Scan0, rgbValues, 0, rgbValues.Length);
            bmp.UnlockBits(bmpData);
            return !rgbValues.Where((v, i) => i%bmpData.Stride < bmp.Width && v != rgbValues[0]).Any();
        }

        private string GetUserTilePath(string username)
        {
            // username: use null for current user
            var sb = new StringBuilder(1000);
            GetUserTilePath(username, 0x80000000, sb, sb.Capacity);
            return sb.ToString();
        }

        private Image GetUserTile(string username)
        {
            return Image.FromFile(GetUserTilePath(username));
        }

        private string GetUserAvatar()
        {
            var avatar = GetUserTile(null);
            var ms = new MemoryStream();
            avatar.Save(ms, ImageFormat.Png);
            var arr = new byte[ms.Length];

            ms.Position = 0;
            ms.Read(arr, 0, (int) ms.Length);
            ms.Close();

            var strBase64 = Convert.ToBase64String(arr);

            return strBase64;
        }

        private string GetUsername()
        {
            return Environment.UserName;
        }

        public void GetWindowsInformation()
        {
            var data = new
            {
                avatar = GetUserAvatar(),
                username = GetUsername()
            };
            serializator.Serialize(client, packet.endpoint, packet.syncKey, data);
        }

        /// <summary>
        ///     Experimental function for monitoring active windows on your remote desktop (windows).
        /// </summary>
        /// <returns></returns>
        public void GetActiveWindowsImages()
        {
            var activeWindows = new List<WindowsImages>();
            foreach (var process in Process.GetProcesses().Where(process => process.MainWindowHandle != IntPtr.Zero))
            {
                RECT rc;
                GetWindowRect(process.MainWindowHandle, out rc);
                if (rc.Width <= 0) continue;
                var bmp = new Bitmap(rc.Width, rc.Height, PixelFormat.Format32bppArgb);
                var gfxBmp = Graphics.FromImage(bmp);
                var hdcBitmap = gfxBmp.GetHdc();

                PrintWindow(process.MainWindowHandle, hdcBitmap, 0);

                gfxBmp.ReleaseHdc(hdcBitmap);
                gfxBmp.Dispose();
                var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                var byteImage = ms.ToArray();
                var base64Window = Convert.ToBase64String(byteImage); //Get Base64
                var image = new WindowsImages
                {
                    imageData = base64Window,
                    windowName = process.ProcessName
                };
                if (!AllOneColor(bmp))
                {
                    activeWindows.Add(image);
                }
            }
            serializator.Serialize(client, packet.endpoint, packet.syncKey, activeWindows);
        }

        #region

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);

        [DllImport("shell32.dll", EntryPoint = "#261",
            CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void GetUserTilePath(
            string username,
            uint whatever, // 0x80000000
            StringBuilder picpath, int maxLength);

        #endregion
    }

    #region

    public class WindowsImages
    {
        public string imageData;
        public string windowName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public RECT(RECT Rectangle) : this(Rectangle.Left, Rectangle.Top, Rectangle.Right, Rectangle.Bottom)
        {
        }

        public RECT(int Left, int Top, int Right, int Bottom)
        {
            X = Left;
            Y = Top;
            this.Right = Right;
            this.Bottom = Bottom;
        }

        public int X { get; set; }

        public int Y { get; set; }

        public int Left
        {
            get { return X; }
            set { X = value; }
        }

        public int Top
        {
            get { return Y; }
            set { Y = value; }
        }

        public int Right { get; set; }

        public int Bottom { get; set; }

        public int Height
        {
            get { return Bottom - Y; }
            set { Bottom = value + Y; }
        }

        public int Width
        {
            get { return Right - X; }
            set { Right = value + X; }
        }

        public Point Location
        {
            get { return new Point(Left, Top); }
            set
            {
                X = value.X;
                Y = value.Y;
            }
        }

        public Size Size
        {
            get { return new Size(Width, Height); }
            set
            {
                Right = value.Width + X;
                Bottom = value.Height + Y;
            }
        }

        public static implicit operator Rectangle(RECT Rectangle)
        {
            return new Rectangle(Rectangle.Left, Rectangle.Top, Rectangle.Width, Rectangle.Height);
        }

        public static implicit operator RECT(Rectangle Rectangle)
        {
            return new RECT(Rectangle.Left, Rectangle.Top, Rectangle.Right, Rectangle.Bottom);
        }

        public static bool operator ==(RECT Rectangle1, RECT Rectangle2)
        {
            return Rectangle1.Equals(Rectangle2);
        }

        public static bool operator !=(RECT Rectangle1, RECT Rectangle2)
        {
            return !Rectangle1.Equals(Rectangle2);
        }

        public override string ToString()
        {
            return "{Left: " + X + "; " + "Top: " + Y + "; Right: " + Right + "; Bottom: " + Bottom + "}";
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        public bool Equals(RECT Rectangle)
        {
            return Rectangle.Left == X && Rectangle.Top == Y && Rectangle.Right == Right && Rectangle.Bottom == Bottom;
        }

        public override bool Equals(object Object)
        {
            if (Object is RECT)
            {
                return Equals((RECT) Object);
            }
            if (Object is Rectangle)
            {
                return Equals(new RECT((Rectangle) Object));
            }

            return false;
        }
    }

    #endregion
}
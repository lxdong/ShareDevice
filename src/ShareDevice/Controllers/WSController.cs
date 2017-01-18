﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Devices;
using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;
using System.Threading;
using System.IO.Compression;
using System.IO;
using ImageSharp;
using ImageSharp.Formats;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace ShareDevice.Controllers
{
    public class WSController : Controller
    {
        public static AndroidDevice ad;
        public static bool isControl;

    
        public async Task Watch() {

            if (Request.HttpContext.WebSockets.IsWebSocketRequest) {
                WatchDevice();
            } else {
                await Request.HttpContext.Response.WriteAsync("请使用Websocekt进行连接!");
            }
        }
      
        public async Task Control() {

            if (Request.HttpContext.WebSockets.IsWebSocketRequest) {
                if (isControl == false) {
                    isControl = true;
                    try {
                        ControlDevice();
                    } catch (Exception) {
                        

                    } finally {
                        isControl = false;
                    }
                    isControl = false;
                } else {
                    WatchDevice();
                }
            } else {
                await Request.HttpContext.Response.WriteAsync("请使用Websocekt进行连接!");
            }
        }


        public IActionResult Error() {
            return View();
        }

        [NonAction]
        private void WatchDevice() {

            using (var webSocket = Request.HttpContext.WebSockets.AcceptWebSocketAsync().Result) {
                bool isPush = false;
                //添加图像输出事件
                var MinicapEvent = ad.AddMinicapEvent(delegate (byte[] imgByte) {
                    if (!isPush) {
                        isPush = true;
                        webSocket.SendAsync(new ArraySegment<byte>(imgByte), WebSocketMessageType.Binary, true, CancellationToken.None).Wait();
                        isPush = false;
                    }
                });


                byte[] buffer = new byte[64];
                var result = webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).Result;


                webSocket.SendAsync(new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes("已经链接手机,请耐心等待图像传输!")), WebSocketMessageType.Text, true, CancellationToken.None).Wait();

                while (true) {
                    byte[] ReceiveBuffer = new byte[64];
                    result = webSocket.ReceiveAsync(new ArraySegment<byte>(ReceiveBuffer), CancellationToken.None).Result;

                    if (result.CloseStatus.HasValue) break;
                }
                ad.RemoveMinicapEvent(MinicapEvent);
                webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None).Wait();
            }


        }



        [NonAction]
        private void ControlDevice() {
            using (var webSocket = Request.HttpContext.WebSockets.AcceptWebSocketAsync().Result) {
                bool isPush = false;
                //添加图像输出事件
                var MinicapEvent = ad.AddMinicapEvent(delegate (byte[] imgByte) {
                    if (!isPush) {
                        isPush = true;
                        webSocket.SendAsync(new ArraySegment<byte>(imgByte), WebSocketMessageType.Binary, true, CancellationToken.None).Wait();
                        isPush = false;
                    }
                });

                IImageEncoder imageEncoder = new JpegEncoder() {
                    Quality = 50,
                    Subsample = JpegSubsample.Ratio420
                };


                var vedio = ZipFile.Open(Path.Combine(Directory.GetCurrentDirectory(), $"Replay/{ad.model}-{ad.deviceName}-{DateTime.Now.ToString("yyyyMMddhhmmss")}.zip"), ZipArchiveMode.Create);
                bool isZipPush = false;
                DateTime lastImgDate = DateTime.Now;
                int imgCnt = 0;
                //添加录像事件
                var ZipEvent = ad.AddMinicapEvent(delegate (byte[] imgByte) {
                    if (!isZipPush) {
                        isZipPush = true;
                        if (imgCnt > 1500) {
                            vedio.Dispose();
                            vedio = ZipFile.Open(Path.Combine(Directory.GetCurrentDirectory(), $"Replay/{ad.model}-{ad.deviceName}-{DateTime.Now.ToString("yyyyMMddhhmmss")}.zip"), ZipArchiveMode.Create);
                            imgCnt = 0;
                        }

                        var nowDate = DateTime.Now;
                        if ((nowDate -lastImgDate).TotalMilliseconds >200) {

                            lastImgDate = nowDate;

                            Image image = new Image(imgByte);

                            //毫秒时间戳
                            var epoch = (nowDate.ToUniversalTime().Ticks - 621355968000000000) / 10000;

                             // 添加jpg
                            var e = vedio.CreateEntry($"{epoch}.jpg", CompressionLevel.Optimal);
                            using (var stream = e.Open()) {
                                image.Save(stream, imageEncoder);
                            }
                            imgCnt++;
                        }
                        isZipPush = false;
                    }
                });



                //第一次通信 暂时不做处理
                byte[] buffer = new byte[64];
                var result = webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).Result;

                webSocket.SendAsync(new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes("已经连接手机,可以进行操控!")), WebSocketMessageType.Text, true, CancellationToken.None).Wait();

                webSocket.SendAsync(new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes("如长时间未显示图像,请尝试点击屏幕或按下home键!")), WebSocketMessageType.Text, true, CancellationToken.None).Wait();


                while (true) {
                    byte[] ReceiveBuffer = new byte[128];
                    result = webSocket.ReceiveAsync(new ArraySegment<byte>(ReceiveBuffer), CancellationToken.None).Result;

                    if (result.CloseStatus.HasValue) break;
                    TouchEvent(ReceiveBuffer);
                }

                ad.RemoveMinicapEvent(MinicapEvent);
                ad.RemoveMinicapEvent(ZipEvent);

                vedio.Dispose();

                webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None).Wait();
                webSocket.Dispose();
            }


        }

        [NonAction]
        /// <summary>
        /// 屏幕操作
        /// </summary>
        /// <param name="buffer"></param>
        private void TouchEvent(byte[] buffer) {
            string str = System.Text.Encoding.UTF8.GetString(buffer);
            var strArry = str.Split(':');

            if (strArry.Length < 2) return;

            switch (strArry[0]) {
                case "3": {
                        var pnt = strArry[1].Split(',');
                        int X = (int)Convert.ToDouble(pnt[0]);
                        int Y = (int)Convert.ToDouble(pnt[1]);
                        ad.TouchMove(X, Y);
                    }
                    break;
                case "1": {
                        var pnt = strArry[1].Split(',');
                        int X = (int)Convert.ToDouble(pnt[0]);
                        int Y = (int)Convert.ToDouble(pnt[1]);
                        ad.TouchDown(X, Y);
                    }
                    break;
                case "2":
                    ad.TouchUp();
                    break;
                case "4":
                    ad.ClickKeycode(Convert.ToInt32(strArry[1]));
                    break;
                default:
                    break;
            }
        }

    }
}
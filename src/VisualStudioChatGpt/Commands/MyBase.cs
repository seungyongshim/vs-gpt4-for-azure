﻿using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;
using EnvDTE;
using System.Windows.Forms;
using System.Collections.Generic;
using VisualStudioChatGpt.Model;
using Newtonsoft.Json;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Net.Http.Headers;

namespace VisualStudioChatGpt.Commands
{
    internal class MyBase
    {
        //定义事件处理器的委托类型
        internal delegate void MyShowEventHandler(string message);
        internal delegate void MyStartEventHandler();
        internal delegate void MyEndEventHandler();

        /// <summary>
        /// 插入节点对象
        /// </summary>
        internal EditPoint insertPoint;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        internal static readonly Guid CommandSet = new Guid("c43b20df-6d16-49bc-b783-8bb7f5c6ff4e");

        internal MyBase()
        {

        }

        /// <summary>
        /// 添加自定义事件
        /// </summary>
        /// <param name="id"></param>
        /// <param name="handler"></param>
        /// <param name="commandService"></param>
        internal void AddEvent(int id, EventHandler handler, OleMenuCommandService commandService)
        {
            var _obj = new CommandID(CommandSet, id);
            var menuItem = new MenuCommand(handler, _obj);
            commandService.AddCommand(menuItem);
        }

        #region 获取选中内容

        /// <summary>
        /// 获取选中内容
        /// </summary>
        /// <returns></returns>
        internal async System.Threading.Tasks.Task<string> GetSelectedTextAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var ret = "";

            DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));
            TextSelection selection = dte.ActiveDocument.Selection as TextSelection;

            if (selection != null && !selection.IsEmpty)
            {
                ret = selection.Text;
            }
            if (string.IsNullOrEmpty(ret))
            {
                MessageBox.Show(TypeModel.Message, "警告提示");
            }
            return ret;
        }
        #endregion 

        #region 选中代码后面插入内容

        /// <summary>
        /// 选中代码后面插入内容
        /// </summary>
        /// <param name="preValue">预先插入值</param>
        /// <param name="word">chatgpt搜索文本内容</param>
        /// <param name="problem">提问信息</param>
        /// <param name="position">插入位置</param>
        /// <returns></returns>
        internal async Task InsertChatGptAsync(string word, InsertPointEnum position)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));
            TextSelection selection = dte.ActiveDocument.Selection as TextSelection;

            if (selection != null && !selection.IsEmpty)
            {
                if (position == InsertPointEnum.Before)//代码前面
                {
                    this.insertPoint = selection.TopPoint.CreateEditPoint();
                }
                else if (position == InsertPointEnum.After)//代码后面
                {
                    this.insertPoint = selection.BottomPoint.CreateEditPoint();
                }
                else if (position == InsertPointEnum.Replace)//注释当前代码 并在后面插入
                {
                    var _insertPoint = selection.TopPoint.CreateEditPoint();
                    _insertPoint.Insert("/*\r\n");

                    _insertPoint = selection.BottomPoint.CreateEditPoint();
                    _insertPoint.Insert("\r\n*/\r\n");

                    this.insertPoint = selection.BottomPoint.CreateEditPoint();
                }
                // 添加事件处理器
                _ = OpenAiAsync(word, VirShowMessage, VirStart, VirEnd);
            }
        }
        #endregion

        #region 插入常量

        /// <summary>
        /// 插入常量
        /// </summary>
        /// <param name="content"></param>
        internal async Task InsertConstAsync(string content, InsertPointEnum position)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));
            TextSelection selection = dte.ActiveDocument.Selection as TextSelection;

            if (selection != null && !selection.IsEmpty)
            {
                if (position == InsertPointEnum.Before)//代码前面
                {
                    var _insertPoint = selection.TopPoint.CreateEditPoint();
                    _insertPoint.Insert(content);
                }
                else if (position == InsertPointEnum.After)//代码后面
                {
                    var _insertPoint = selection.BottomPoint.CreateEditPoint();
                    _insertPoint.Insert(content);
                }
                else if (position == InsertPointEnum.Replace)//注释当前代码 并在后面插入
                {
                    var _insertPoint = selection.TopPoint.CreateEditPoint();
                    _insertPoint.Insert("/*\n");

                    _insertPoint = selection.BottomPoint.CreateEditPoint();
                    _insertPoint.Insert("\n*/\n");

                    _insertPoint = selection.BottomPoint.CreateEditPoint();
                    _insertPoint.Insert(content);
                }
            }
        }
        #endregion

        #region 格式化代码

        /// <summary>
        /// 格式化代码
        /// </summary>
        internal async Task SimulateCtrlKCtrlDAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));
            dte.ExecuteCommand("Edit.FormatDocument");
        }
        #endregion

        #region 请求OpenAI 获取数据

        /// <summary>
        /// 请求OpenAI 获取数据
        /// </summary>
        /// <param name="url"></param>
        /// <param name="postData"></param>
        /// <param name="ltcid"></param> 
        /// <param name="timeout">默认超时 5秒</param> 
        /// <returns></returns>
        internal static async Task OpenAiAsync(string word, MyShowEventHandler showEvent, MyStartEventHandler startEvent, MyEndEventHandler endEvent)
        {
            await ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                var config = MyConfig.Get();
                if (string.IsNullOrEmpty(config.apikey))
                {
                    MessageBox.Show("请设置OpenAI key");

                    FormSetUp.Instance.Show();
                    FormSetUp.Instance.Activate();
                    return;
                }

                var par = new
                {
                    model = config.model,
                    temperature = Convert.ToDouble(config.temperature),
                    stream = true,
                    max_tokens = Convert.ToInt32(config.maxtoken),
                    messages = new List<object> { new { role = "user", content = word } }
                };

                HttpClientHandler handler = new HttpClientHandler();// 创建HttpClientHandler实例
                if (!string.IsNullOrEmpty(config.proxy))
                {
                    handler.Proxy = new WebProxy(config.proxy);// 设置代理服务器地址和端口
                }
                using (HttpClient httpClient = new HttpClient(handler))
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, config.apiurl);
                    request.Content = new StringContent(JsonConvert.SerializeObject(par), Encoding.UTF8);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    if (config.serviceName == ServiceEnum.Azure.ToString())//微软Azure云
                    {
                        request.Content.Headers.Add("api-key", $"{config.apikey}");
                    }
                    else//OpenAI
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{config.apikey}");
                    }

                    using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))//发送请求并获取响应
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            startEvent.Invoke();//开始

                            response.EnsureSuccessStatusCode();
                            using (Stream responseStream = await response.Content.ReadAsStreamAsync())
                            using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
                            {
                                while (!reader.EndOfStream)//逐行读取响应流
                                {
                                    string line = await reader.ReadLineAsync();
                                    if (!string.IsNullOrEmpty(line) && line.Contains("content"))
                                    {
                                        line = line.Remove(0, 5);
                                        var obj = JsonConvert.DeserializeObject<dynamic>(line);
                                        var temp = obj["choices"][0]["delta"]["content"].ToString();
                                        showEvent.Invoke(temp);//插入gpt结果 
                                        await Task.Delay(1);
                                    }
                                }
                            }
                            endEvent.Invoke();//结束
                        }
                        else
                        {
                            string line = await response.Content.ReadAsStringAsync();
                            var obj = JsonConvert.DeserializeObject<dynamic>(line);
                            var message = obj["error"]["message"].ToString();
                            if (string.IsNullOrEmpty(message))
                            {
                                message = obj["error"]["code"].ToString();
                            }
                            MessageBox.Show(message);

                            FormSetUp.Instance.Show();
                            FormSetUp.Instance.Activate();
                        }
                    }
                }
            });
        }
        #endregion

        #region 虚方法

        /// <summary>
        /// 自定义处理事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        internal virtual async void VirHandler(object sender, EventArgs e)
        {
            MessageBox.Show("未实现此功能!");
        }

        /// <summary>
        /// 插入代码
        /// </summary>
        /// <param name="message"></param>
        internal virtual void VirShowMessage(string message)
        {
            if (this.insertPoint != null)
            {
                this.insertPoint.Insert(message);
            }
        }

        /// <summary>
        /// 执行开始
        /// </summary>
        /// <param name="message"></param>
        internal virtual void VirStart()
        {
        }

        /// <summary>
        /// 执行完成
        /// </summary>
        /// <param name="message"></param>
        internal virtual void VirEnd()
        {
        }
        #endregion
    }
}

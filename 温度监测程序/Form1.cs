﻿using System;
using System.Drawing;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using 温度监测程序.MonitoringSystem.common;
using 温度监测程序.MonitoringSystem.pojo;


namespace 温度监测程序
{
    public partial class Form1 : Form
    {
        public delegate void delegateReply(ushort[] dataValue, byte code, int seat, object[] setAs, ushort startRegister, string msg);
        private ChartClass chartData1;
        private ChartClass chartData2;
        private System.Timers.Timer timerReadData = new System.Timers.Timer(200.0);
        private System.Timers.Timer timerCom = new System.Timers.Timer(2000.0);
        private System.Timers.Timer timerSaveData = new System.Timers.Timer();
        private System.Timers.Timer timerSendData = new System.Timers.Timer(500.0);
        public delegateReply ReplyDelegate;
        private string portName;
        private int baudRate;
        private Parity parity;
        private UdpClient udpClient;
        private IPEndPoint remoteEndPoint;
        private ModbusTools tool;
        private ModbusDataExhibit exhibit;
        private RichTextBox LooktextBox;
        private bool correspondModel = true;
        private bool readDataBox = true;
        private bool adjusting = true;
        private byte slaveAddress = 1;
        private ExcelHelpClass excelHelp;
        private float fTemperature;
        private float fHumidity;
        private Chart chart1;
        private Chart chart2;
        private Thread sendThread;

        public Form1()
        {
            InitializeComponent();
            ckCbx.SelectedIndex = 14;

            string serverIp = "172.22.50.3";
            int serverPort = 49200;
            remoteEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);

            tool = new ModbusTools();
            exhibit = new ModbusDataExhibit();
            excelHelp = new ExcelHelpClass();
            chartData1 = new ChartClass(21);
            chart1 = new Chart();
            chartData2 = new ChartClass(21);
            udpClient = new UdpClient();

            timerReadData.Elapsed += TimerReadMethod;
            timerReadData.AutoReset = true;

            timerSendData.Elapsed += TimerSendMethod;
            timerSendData.AutoReset = true;

            ReplyDelegate = ResponseData;
        }
        public float GetTemperature()
        {
            return fTemperature;
        }

        public float GetHumidity()
        {
            return fHumidity;
        }
        private void startGetTempAndHumi()
        {
            portName = ckCbx.SelectedItem.ToString();
            baudRate = 9600;
            try
            {
                tool.setPort(portName, baudRate, Parity.None, 8, StopBits.One);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"请检查串口: {portName} 是否被占用。");
                button1.Text = "开始";
                return;
            }
            tool.startUpMethod(this, 1);
            timerReadData.Enabled = true;
            timerSendData.Enabled = true;
        }
        public void readMethod()
        {
            timerReadData.Enabled = false;
            if (correspondModel && adjusting)
            {
                if (tool.ThreadStateData && tool.ReadCount() < 5)
                {
                    tool.AddListRead(new ModbusClass(slaveAddress, 0, 2, 4, 4));
                }
                if (readDataBox)
                {
                    DisplayInstruction(true, 4, 0, 2);
                }
            }
            if (tool.ThreadStateData)
            {
                timerReadData.Enabled = true;
            }
        }
        private void TimerReadMethod(object source, ElapsedEventArgs e)
        {
            ThreadSafe(delegate
            {
                readMethod();
            });
        }

        public void sendMethod()
        {
            string message = $"温度:{string.Format("{0:f1}", fTemperature)}℃ 湿度:{string.Format("{0:f1}", fHumidity)}％";
            byte[] data = Encoding.UTF8.GetBytes(message);
            // MessageBox.Show(message+data);
            try
            {
                udpClient.Send(data, data.Length, remoteEndPoint);
            }
            catch (Exception)
            {
            }
        }
        private void TimerSendMethod(object sender, ElapsedEventArgs e)
        {
            ThreadSafe(delegate
            {
                sendMethod();
            });
        }
        private void ThreadSafe(MethodInvoker method)
        {
            try
            {
                if (base.InvokeRequired)
                {
                    Invoke(method);
                }
                else
                {
                    method();
                }
            }
            catch (Exception)
            {
            }
        }
        public void DisplayInstruction(bool ir, byte code, ushort StartRegister, ushort dataSon)
        {
            DisplayInstruction(ir, code, StartRegister, dataSon, null, LooktextBox);
        }

        public void DisplayInstruction(bool ir, byte code, ushort StartRegister, ushort[] data)
        {
            DisplayInstruction(ir, code, StartRegister, 0, data, LooktextBox);
        }

        public void DisplayInstruction(string msg, bool ir)
        {
            ThreadSafe(delegate
            {
                exhibit.AddTextBox(LooktextBox, msg, ir ? "发" : "收", ir ? Color.LightSkyBlue : Color.MediumSeaGreen);
            });
        }

        public void DisplayInstruction(bool ir, byte code, ushort StartRegister, ushort dataSon, ushort[] data, RichTextBox LookTextBox)
        {
            string RMdata = string.Empty;
            if (ir)
            {
                switch (code)
                {
                    case 1:
                    case 2:
                    case 3:
                    case 4:
                    case 5:
                    case 6:
                        RMdata = exhibit.String1And6Data(slaveAddress, code, StartRegister, dataSon, "→发");
                        break;
                }
            }
            else
            {
                switch (code)
                {
                    case 5:
                    case 6:
                    case 15:
                    case 16:
                        RMdata = exhibit.String1And6Data(slaveAddress, code, StartRegister, dataSon, "←收");
                        break;
                    case 3:
                    case 4:
                        RMdata = exhibit.String3And4Data(slaveAddress, code, data, "←收");
                        break;
                }
            }
            ThreadSafe(delegate
            {
                exhibit.AddTextBox(LookTextBox, RMdata, ir ? "发" : "收", ir ? Color.LightSkyBlue : Color.MediumSeaGreen);
            });
        }

        private float RegValue2Temp(ushort nValue)
        {
            float fValue = 0f;
            fValue = ((((nValue >> 13) & 1) == 1) ? 1 : 0);
            fValue = ((fValue == 0f) ? ((float)(int)nValue) : ((float)(-(nValue - 10000))));
            return fValue / 10f;
        }

        public void SansResponseData(ushort[] data, byte code, int seat, object[] setAs, ushort startRegister, string msg)
        {
            try
            {
                if (base.InvokeRequired)
                {
                    Invoke(ReplyDelegate, data, code, seat, setAs, startRegister, msg);
                }
                else
                {
                    ResponseData(data, code, seat, setAs, startRegister, msg);
                }
            }
            catch (Exception)
            {
            }
        }

        public void ResponseData(ushort[] data, byte code, int seat, object[] setAs, ushort startRegister, string msg)
        {
            switch (seat)
            {
                case 0:
                    if (msg != null && msg.Length > 0)
                    {
                        DisplayInstruction(msg, false);
                    }
                    break;
                case 4:
                    if (data != null && data.Length >= 0)
                    {
                        fTemperature = RegValue2Temp(data[0]);
                        labelTemperatureCH1.Text = string.Format("{0:f1}", fTemperature);
                        fHumidity = RegValue2Temp(data[1]);
                        labelHumidityCH1.Text = string.Format("{0:f1}", fHumidity);
                        // chartData1.PointDisp(fTemperature, chart1.Series[0]);
                        // chartData2.PointDisp(fHumidity, chart2.Series[0]);
                        if (readDataBox)
                        {
                            DisplayInstruction(false, code, 0, data);
                        }
                    }
                    break;
                case 5:
                    DisplayInstruction(false, code, startRegister, data[0]);
                    break;
                case 6:
                    if (data != null && data.Length != 0 && setAs != null && setAs.Length != 0)
                    {
                        DisplayInstruction(false, code, startRegister, data[0]);
                        if ((ushort)setAs[0] == data[0])
                        {
                            object obj = setAs[2];
                            MessageBox.Show(((obj != null) ? obj.ToString() : null) + "设置成功！");
                        }
                        else
                        {
                            object obj2 = setAs[2];
                            MessageBox.Show(((obj2 != null) ? obj2.ToString() : null) + "设置失败！");
                        }
                    }
                    break;
                case 1:
                case 2:
                case 3:
                    break;
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            closeModel();
            timersStop();
            // tool.closurePort();
            Application.Exit();
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            Dispose();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (button1.Text == "停止")
            {
                timersStop();
                tool.closurePort();
                button1.Text = "开始";
                labelHumidityCH1.Text = "0.0";
                labelTemperatureCH1.Text = "0.0";
            }
            else
            {
                button1.Text = "停止";
                startGetTempAndHumi();
            }

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Form2 form2 = new Form2(this);
            form2.Show();

            startGetTempAndHumi();
            lbbbh.Text = $"V {Assembly.GetExecutingAssembly().GetName().Version}";
            label7.Text = $"{tool.getStationName()}{Environment.MachineName.Substring(Math.Max(0, Environment.MachineName.Length - 6), 6)}";
        }

        private void timersStop()
        {
            timerReadData.Enabled = false;
            timerSendData.Enabled = false;
        }

        public void closeModel()
        {
            try
            {
                if (tool != null)
                {
                    tool.destroyThread();
                }
                /*threadStateData = false;
                if (childThread != null)
                {
                    childThread.Abort();
                    childThread = null;
                }
                if (excelHelp != null)
                {
                    excelHelp.AppendData(WriteWorkbook, saveFileDialog, tableData);
                }
                timerCom.Enabled = false;*/
            }
            catch (Exception)
            {
                Environment.Exit(0);
            }
        }

    }
}

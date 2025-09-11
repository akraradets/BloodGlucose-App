using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace backend_dotnet.Utils;

internal class SerialService
{
    /// <summary>
    /// 即插即用设备信息结构
    /// </summary>
    public struct PnPEntityInfo
    {
        public String PNPDeviceID;      // 设备ID
        public String Name;             // 设备名称
        public String Description;      // 设备描述
        public String Service;          // 服务
        public String Status;           // 设备状态
        public UInt16 VendorID;         // 供应商标识
        public UInt16 ProductID;        // 产品编号 
        public Guid ClassGuid;          // 设备安装类GUID
    }

    /// <summary>
    /// 串口是否打开
    /// <see cref="http://blog.csdn.net/jhqin/article/details/6734673"/>
    /// </summary>
    public static bool Is_Port_Open { get; private set; }

    /// <summary>
    /// 串口对象，目前是静态的
    /// </summary>
    private static SerialPort serialPort = new SerialPort();

    /// <summary>
    /// 接收计数
    /// </summary>
    public int received_count = 0;

    public IEnumerable<string> GetComList()
    {
        string[] sSubKeys = SerialPort.GetPortNames();

        for (int i = 0; i < sSubKeys.Length; i++)
        {
            yield return sSubKeys[i];
        }
    }

    public bool Get_Device_Status()
    {
        return Is_Port_Open;
    }

    public bool Serial_Port_Open(string Port_Name)
    {

        bool result = true;
        try
        {
            serialPort.PortName = Port_Name;
            serialPort.BaudRate = 115200;
            serialPort.DataBits = 8;
            serialPort.Parity = Parity.None;
            serialPort.StopBits = StopBits.One;
            serialPort.ReadTimeout = 5000;
            serialPort.Open();

            if (serialPort.IsOpen)
            {
                Is_Port_Open = true;
            }
            else
            {
                serialPort.Close();
                result = false;
            }
        }
        catch (Exception ex)
        {
            result = false;
            Is_Port_Open = false;
        }

        return result;
    }

    public void Serial_Port_Close()
    {
        try
        {
            int tickCount = Environment.TickCount;

            serialPort.Close();
            Is_Port_Open = false;
        }
        catch (Exception ex)
        {

        }
    }

    /// <summary>
    /// 获取串口返回数据
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns></returns>
    public bool Serial_Receive(List<byte> buffer)
    {
        try
        {
            int n = 0, try_time = 0, len = 0;

            do
            {
                try_time++;

                if (try_time > 1)
                {
                    Thread.Sleep(200);
                }

                n = serialPort.BytesToRead;

                byte[] readBuffer = new byte[n];
                serialPort.Read(readBuffer, 0, n);

                received_count += n;//增加接收计数

                //1.缓存数据
                buffer.AddRange(readBuffer);

                //2.完整性判断
                if (received_count < 4)
                    continue;

                len = (buffer[2] << 8) + buffer[3] + 2;

                if (buffer[0] == 0xaa && buffer[1] == 0x55 && len == received_count)
                {
                    received_count = 0;
                    return true;
                }
                else if (buffer[4] == 0x06 || buffer[4] == 0x04)
                {
                    received_count = 0;
                    return true;
                }

            } while (try_time < 4);
        }
        catch (Exception ex)
        {

        }

        received_count = 0;

        return false;
    }

    public void Serial_Send(byte[] data)
    {
        try
        {
            serialPort.Write(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            Is_Port_Open = false;
        }
    }

    /// <summary>
    /// 查询USB设备实体（设备要求有VID和PID）
    /// </summary>
    /// <param name="vendorID">供应商标识，MinValue忽视</param>
    /// <param name="productID">产品编号，MinValue忽视</param>
    /// <param name="ClassGuid">设备安装类Guid，Empty忽视</param>
    /// <returns>设备列表</returns>
    public string WhoUsbDevice(UInt16 vendorID, UInt16 productID)  //public static PnPEntityInfo[] WhoUsbDevice(UInt16 VendorID, UInt16 ProductID, Guid ClassGuid)
    {
        List<PnPEntityInfo> usbDevices = new List<PnPEntityInfo>();

        int lenght = 0;
        PnPEntityInfo element;
        string serialname = "";

        // 获取USB控制器及其相关联的设备实体
        var usbControllerDeviceCollection = new ManagementObjectSearcher("SELECT * FROM Win32_USBControllerDevice").Get();

        if (usbControllerDeviceCollection == null) return null;

        foreach (var usbControllerDevice in usbControllerDeviceCollection)
        {
            // 获取设备实体的DeviceID
            String Dependent = (usbControllerDevice["Dependent"] as String).Split(new Char[] { '=' })[1];

            // 过滤掉没有VID和PID的USB设备
            Match match = Regex.Match(Dependent, "VID_[0-9|A-F]{4}&PID_[0-9|A-F]{4}");

            if (!match.Success) continue;

            UInt16 theVendorID = Convert.ToUInt16(match.Value.Substring(4, 4), 16);   // 供应商标识
            if (vendorID != UInt16.MinValue && vendorID != theVendorID) continue;

            UInt16 theProductID = Convert.ToUInt16(match.Value.Substring(13, 4), 16); // 产品编号
            if (productID != UInt16.MinValue && productID != theProductID) continue;

            // 即插即用
            ManagementObjectCollection pnpEntityCollection = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE DeviceID=" + Dependent).Get();
            if (pnpEntityCollection == null) continue;

            foreach (ManagementObject entity in pnpEntityCollection)
            {

                Guid theClassGuid = new Guid(entity["ClassGuid"] as String);    // 设备安装类GUID

                element.PNPDeviceID = entity["PNPDeviceID"] as String;  // 设备ID
                element.Name = entity["Name"] as String;                // 设备名称
                element.Description = entity["Description"] as String;  // 设备描述
                element.Service = entity["Service"] as String;          // 服务
                element.Status = entity["Status"] as String;            // 设备状态
                element.VendorID = theVendorID;                         // 供应商标识
                element.ProductID = theProductID;                       // 产品编号
                element.ClassGuid = theClassGuid;                       // 设备安装类GUID

                usbDevices.Add(element);

                lenght = element.Name.Length;

                #region Get serial name

                if (lenght == 42)
                {
                    serialname = element.Name.Substring(lenght - 5, 4);//
                }

                else if (lenght == 43)
                {
                    serialname = element.Name.Substring(lenght - 6, 5);
                }
                else if (lenght == 44)
                {
                    serialname = element.Name.Substring(lenght - 7, 6);
                }
                else
                {
                    serialname = element.Name.Substring(lenght - 6, 5);
                }

                #endregion

                if (usbDevices.Count == 0)
                {
                    return null;
                }
                else
                {
                    return serialname;
                }
            } // end of 即插即用
        } // end of 获取USB控制器及其相关联的设备实体

        if (usbDevices.Count == 0)
        {
            return null;
        }
        else
        {
            return serialname;
        }
    }

    public byte ReceiveMotorReturnStatus()
    {
        byte[] receivedData = new byte[10];
        byte framHeadH;
        byte framHeadL;
        byte framLengthH;
        byte framLengthL;
        ushort framLength;
        byte command;
        byte status;
        byte cke = 0;
        byte ckereceive = 0;

        framHeadH = (byte)serialPort.ReadByte();
        if (framHeadH != 0xAA) return 0xFE;
        ckereceive += framHeadH;

        framHeadL = (byte)serialPort.ReadByte();
        if (framHeadL != 0x55) return 0xFE;
        ckereceive += framHeadL;

        framLengthH = (byte)serialPort.ReadByte();
        ckereceive += framLengthH;

        framLengthL = (byte)serialPort.ReadByte();
        ckereceive += framLengthL;

        framLength = (ushort)((ushort)framLengthH * (ushort)256 + (ushort)framLengthL);

        command = (byte)serialPort.ReadByte();
        ckereceive += command;

        for (int i = 0; i < framLength - 4; i++)
        {
            receivedData[i] = (byte)serialPort.ReadByte();
            ckereceive += receivedData[i];
        }

        status = receivedData[0];
        cke = (byte)serialPort.ReadByte();
        return status;
    }
}

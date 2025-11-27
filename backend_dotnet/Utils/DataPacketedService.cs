using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace backend_dotnet.Utils;

internal class DataPacketedService
{
    public DataPacketedService(SerialService serialService)
    {
        serial_ops = serialService;
    }

    private SerialService serial_ops;

    private List<Byte> sdatapacket = new List<Byte>();
    public static List<Byte> rdata = new List<Byte>();
    public static List<Byte> rdarkdata = new List<Byte>();

    private static byte rtemperture_cmd = 0x01;//读取模块温度
    private static byte rmodulever_cmd = 0x02;//读取软件版本
    private static byte rpn_cmd = 0x03;//读取pn号
    private static byte rsn_cmd = 0x04;//读取sn号
    private static byte wsn_cmd = 0x05;//设置sn号
    private static byte rdate_cmd = 0x06;//读取出厂日期
    private static byte rinf_cmd = 0x07;//读取模块信息
    private static byte wdate_cmd = 0x08;//设置出厂日期
    private static byte rman_cmd = 0x09;//读取厂家信息
    private static byte rvol_cmd = 0x10;//读取模块电压
    private static byte wtectmp_cmd = 0x12;//设置TEC温度
    private static byte rtectmp_cmd = 0x13;//读取TEC温度
    private static byte wccdexpo_cmd = 0x14;//设置CCD曝光时间
    private static byte wccdscan_cmd = 0x16;//设置CCD开始扫描
    private static byte rccddata_cmd = 0x17;//读取CCD数据
    private static byte wldcurrent_cmd = 0x20;//设置LD电流
    private static byte rccddark_cmd = 0x23;//读取CCD暗电流
    private static byte rteclock_cmd = 0x19;//读取TEC锁定状态

    private static byte LOAD_Calibration_DATA = 0x30;
    private static byte READ_Calibration_DATA = 0x31;

    private static byte LOAD_Calibration_DATA_PSNM = 0x34;
    private static byte READ_Calibration_DATA_PSNM = 0x35;

    private static byte SetLight_cmd = 0x2D;
    private static byte SetXMotor_cmd = 0x2E;
    private static byte SetYMotor_cmd = 0x2F;
    private static byte SetZMotor_cmd = 0x2B;
    private static byte SetSwitchMotor_cmd = 0x24;

    private static int CCD_DATA_PACK_SIZE = 3648;
    public static int Expose_Time = 1000;
    public static int Scan_time = 1;
    public static string Smooth_Level = "NONE";
    public static int nBoxWidth = 0;

    public static int integraltime_num = 1;//计算积分时间累加次数，60s递进累加，自动积分时间的话785最大60s，1064最大120s
    public static int integraltime_yushu = 0;//计算积分时间累加次数，60s递进累加，余数
    public static int integraltime_add = 65000;//计算积分时间累加次数，60s递进累加，

    public static int Ldcurrent = 0;

    public static int Tectemp = 0;

    public static double[] KDark;
    public static double[] KRaw;
    public static double[] KRaw_Dark;

    static DataPacketedService()
    {
        CCD_DATA_PACK_SIZE = 3648;
        KDark = new double[CCD_DATA_PACK_SIZE];
        KRaw = new double[CCD_DATA_PACK_SIZE];
        KRaw_Dark = new double[CCD_DATA_PACK_SIZE];
    }

    private void Sdata_head()
    {
        sdatapacket.Add((byte)0xaa);
        sdatapacket.Add((byte)0x55);
    }

    private void Sdata_len(int len)
    {
        sdatapacket.Add((byte)((len >> 8) & 0xff));
        sdatapacket.Add((byte)((len) & 0xff));
    }

    private void Sdata_cmd(byte cmd)
    {
        sdatapacket.Add(cmd);
    }
    private void Sdata_content(byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            sdatapacket.Add(data[i]);
        }
    }

    private void Sdata_checksum()
    {
        int check_sum = 0;
        for (int i = 2; i < sdatapacket.Count; i++)
        {

            check_sum += sdatapacket[i];
            check_sum &= 0xff;
        }
        sdatapacket.Add((byte)check_sum);
    }

    private void Generate_packet_bycmd(byte cmd)
    {
        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4);
        Sdata_cmd(cmd);
        Sdata_checksum();
    }

    #region 显微拉曼functin

    /// <summary>
    /// 设置输出电压控制灯亮度
    /// </summary>
    /// <param name="voltage"></param>
    /// <returns></returns>
    public byte SetLightVoltage(int voltage)
    {
        byte[] transmitData = new byte[2];
        transmitData[0] = (byte)(voltage >> 8);
        transmitData[1] = (byte)voltage;

        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + transmitData.Length);
        Sdata_cmd(SetLight_cmd);
        Sdata_content(transmitData);
        Sdata_checksum();

        if (voltage >= 0 && voltage <= 25)
        {
            Data_Packeted_Send();
            return 0x00;
        }
        else
        {
            return 0x01;
        }
    }

    /// <summary>
    /// 设置z轴电机 down:0x00 up:0xff 下限位1 上限位2
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="angelnum"></param>
    /// <returns></returns>
    public byte SetZMotor_Step(byte direction, int angelnum)
    {
        byte[] transmitData = new byte[3];

        transmitData[0] = direction;
        transmitData[1] = (byte)(angelnum >> 8);
        transmitData[2] = (byte)angelnum;
        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + transmitData.Length);
        Sdata_cmd(SetZMotor_cmd);
        Sdata_content(transmitData);
        Sdata_checksum();


        if (angelnum >= 0 && angelnum <= 360 && (direction == 0x00 || direction == 0xff))
        {
            Data_Packeted_Send();

            var status = serial_ops.ReceiveMotorReturnStatus();

            return status; //限位状态  1 限位    0 没限位
        }
        else
        {
            //角度和方向错误
            throw new ArgumentException($"direction:{nameof(direction)} angel:{nameof(angelnum)}");
        }
    }

    /// <summary>
    /// 设置X轴电机步进 Legacy，旧电机没有限位
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="angelnum"></param>
    /// <returns></returns>
    public byte SetXMotor_StepLegacy(byte direction, int angelnum)
    {
        byte[] transmitData = new byte[3];

        transmitData[0] = direction;
        transmitData[1] = (byte)(angelnum >> 8);
        transmitData[2] = (byte)angelnum;
        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + transmitData.Length);
        Sdata_cmd(SetXMotor_cmd);
        Sdata_content(transmitData);
        Sdata_checksum();

        Data_Packeted_Send();

        return 0x0;
    }

    /// <summary>
    /// 设置X轴电机步进
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="angelnum"></param>
    /// <returns></returns>
    public byte SetXMotor_Step(byte direction, int angelnum)
    {
        byte[] transmitData = new byte[3];

        transmitData[0] = direction;
        transmitData[1] = (byte)(angelnum >> 8);
        transmitData[2] = (byte)angelnum;
        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + transmitData.Length);
        Sdata_cmd(SetXMotor_cmd);
        Sdata_content(transmitData);
        Sdata_checksum();


        if (angelnum >= 0 && angelnum <= 360 && (direction == 0x00 || direction == 0xff))
        {
            Data_Packeted_Send();
            byte status = serial_ops.ReceiveMotorReturnStatus();

            return status;//限位状态  1 限位    0 没限位
        }
        else
        {
            return 0x01;
        }
    }

    /// <summary>
    /// 设置Y轴电机步进 Legacy, 旧电机没有限位
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="angelnum"></param>
    /// <returns></returns>
    public byte SetYMotor_StepLegacy(byte direction, int angelnum)
    {
        byte[] transmitData = new byte[3];

        transmitData[0] = direction;
        transmitData[1] = (byte)(angelnum >> 8);
        transmitData[2] = (byte)angelnum;
        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + transmitData.Length);
        Sdata_cmd(SetYMotor_cmd);
        Sdata_content(transmitData);
        Sdata_checksum();
        Data_Packeted_Send();

        return 0x0;
    }

    /// <summary>
    /// 设置Y轴电机步进
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="angelnum"></param>
    /// <returns></returns>
    public byte SetYMotor_Step(byte direction, int angelnum)
    {
        byte[] transmitData = new byte[3];

        transmitData[0] = direction;
        transmitData[1] = (byte)(angelnum >> 8);
        transmitData[2] = (byte)angelnum;
        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + transmitData.Length);
        Sdata_cmd(SetYMotor_cmd);
        Sdata_content(transmitData);
        Sdata_checksum();

        if (angelnum >= 0 && angelnum <= 360 && (direction == 0x00 || direction == 0xff))
        {
            Data_Packeted_Send();

            byte status = serial_ops.ReceiveMotorReturnStatus();

            return status;//限位状态  1 限位    0 没限位
            //return 0;
        }
        else
        {
            return 0x01;
        }
    }

    /// <summary>
    /// 旧电机设置步进电机电源
    /// </summary>
    /// <param name="switchstatus"></param>
    /// <returns></returns>
    public byte SetSwitchMotor_Legacy(byte switchstatus)
    {
        byte[] transmitData = new byte[1];

        transmitData[0] = switchstatus;

        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + transmitData.Length);
        Sdata_cmd(SetSwitchMotor_cmd);
        Sdata_content(transmitData);
        Sdata_checksum();

        Data_Packeted_Send();

        return 0x00;
    }

    #endregion

    private void Set_module_SN_packet(byte[] data)
    {
        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + data.Length);
        Sdata_cmd(wsn_cmd);
        Sdata_content(data);
        Sdata_checksum();
    }

    private void Set_module_date_packet(byte[] data)
    {
        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + data.Length);
        Sdata_cmd(wdate_cmd);
        Sdata_content(data);
        Sdata_checksum();
    }

    /// <summary>
    /// 设置TEC温度
    /// </summary>
    /// <param name="data"></param>
    private void Set_ccd_tectmp_packet(byte[] data)
    {
        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + data.Length);
        Sdata_cmd(wtectmp_cmd);
        Sdata_content(data);
        Sdata_checksum();
    }

    private void Set_ccd_expo_packet(byte[] data)
    {
        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + data.Length);
        Sdata_cmd(wccdexpo_cmd);
        Sdata_content(data);
        Sdata_checksum();
    }

    private void Start_ccd_scan_packet(byte mode)
    {
        sdatapacket.Clear();
        byte[] data = new byte[2];
        Sdata_head();
        Sdata_len(6);
        Sdata_cmd(wccdscan_cmd);
        data[0] = mode;
        data[1] = 0x01;
        Sdata_content(data);
        Sdata_checksum();
    }

    private void Set_Ld_Current_Packet(byte[] data)
    {
        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + data.Length);
        Sdata_cmd(wldcurrent_cmd);
        Sdata_content(data);
        Sdata_checksum();
    }

    private void Read_ccd_data_packet()
    {
        sdatapacket.Clear();
        byte[] data = new byte[1];
        Sdata_head();
        Sdata_len(5);
        Sdata_cmd(rccddata_cmd);
        data[0] = 0x00;
        Sdata_content(data);
        Sdata_checksum();
    }

    public bool Have_Receive_Data()
    {

        int checksum = 0;
        for (int i = 2; i < rdata.Count - 1; i++)
        {
            checksum += rdata[i];
            checksum &= 0xff;
        }
        if (checksum == rdata[rdata.Count - 1])
        {
            rdata.RemoveAt(0);
            rdata.RemoveAt(0);
            rdata.RemoveAt(rdata.Count - 1);
            return true;
        }
        else
            return false;


    }

    /// <summary>
    /// 获取串口返回数据
    /// </summary>
    /// <returns></returns>
    private bool Do_Waiting_Receive()
    {
        rdata.Clear();

        bool result = serial_ops.Serial_Receive(rdata);
        if (!result)
        {
            return false;
        }

        result = Have_Receive_Data();

        return result;
    }

    private int Check_Receive_Data()
    {

        int len = (rdata[0] << 8) + rdata[1] - 2;
        if (len < 3)
            return 1;
        rdata.RemoveAt(0);
        rdata.RemoveAt(0);
        byte cmd = rdata[0];
        rdata.RemoveAt(0);
        byte response = rdata[0];
        if (response == 0x00)
            return 0;

        else if (response == 0xff)
            return 0xff;
        else if (response == 0x2d || response == 0x30)
            return 0;
        else
            return 0;
    }

    private void Data_Packeted_Send()
    {


        byte[] tsdata = sdatapacket.ToArray();
        serial_ops.Serial_Send(tsdata);

    }

    /// <summary>
    /// 获取返回数据，会重发命令
    /// </summary>
    /// <returns></returns>
    private bool Do_Waiting_Receive_Ok()
    {
        if (SerialService.Is_Port_Open == false)
        {
            return false;
        }

        var eta = DateTime.Now.AddMilliseconds(Expose_Time + 3000);

        int try_count = 0;
        int countmax = 0;
        while (true)
        {
            if (!Do_Waiting_Receive())
            {
                Thread.Sleep(200);
                Data_Packeted_Send();
                try_count++;


                countmax = Expose_Time / 50 + 550;

                if (try_count < countmax && DateTime.Now <= eta)
                    continue;
                else
                    return false;
            }

            int result = Check_Receive_Data();
            if (result == 1)
            {
                return false;
            }
            if (result == 0xff)
            {

                countmax = Expose_Time / 50 + 600;

                Thread.Sleep(500);

                Data_Packeted_Send();
                try_count++;

                if (try_count < countmax && DateTime.Now <= eta)
                    continue;
                else
                    return false;
            }
            else
                break;

        }
        return true;
    }

    public bool Set_Exposed_Time()
    {
        // rdarkdata.Clear();
        byte[] data = new byte[2];
        data[0] = (byte)((Expose_Time >> 8) & 0xff);
        data[1] = (byte)(Expose_Time & 0xff);
        Set_ccd_expo_packet(data);
        Data_Packeted_Send();

        return Do_Waiting_Receive_Ok();
    }

    public bool Set_Ld_Current()
    {
        byte[] data = new byte[2];

        data[0] = (byte)((Ldcurrent >> 8) & 0xff);
        data[1] = (byte)(Ldcurrent & 0xff);
        Set_Ld_Current_Packet(data);

        Data_Packeted_Send();

        return Do_Waiting_Receive_Ok();
    }

    /****************************************************************
    * 函数名称: Read_ModuleSN
    * 功能描述: 读取模块SN号
    * 输入参数: 
    * 输出参数:  
    * 全局变量: none                                                    
    * 调用模块:     
    * 作　  者: 
    * 创建日期: 2016年05月07日
    * 修改日志：
    *****************************************************************/
    public Tuple<bool, string> Read_ModuleSN()
    {
        byte[] data = new byte[1];
        byte[] refdata = new byte[8];
        sdatapacket.Clear();

        Sdata_head();
        Sdata_len(4);
        Sdata_cmd(rsn_cmd);
        Sdata_checksum();

        Data_Packeted_Send();

        if (!Do_Waiting_Receive_Ok())
            return Tuple.Create(false, (string)null);

        for (int i = 0; i < rdata.Count; i++)
        {
            refdata[i] = rdata[i];
        }

        var module = System.Text.Encoding.ASCII.GetString(refdata);

        return Tuple.Create(true, module);
    }

    /****************************************************************
    * 函数名称: Set_Tec_Tmp
    * 功能描述: 设置TEC温度值
    * 输入参数: 
    * 输出参数:  
    * 全局变量: none                                                    
    * 调用模块:     
    * 作　  者: 
    * 创建日期: 2016年03月17日
    * 修改日志：
    *****************************************************************/
    public bool Set_Tec_Tmp()
    {
        byte[] data = new byte[2];
        if (Tectemp < 0)
            data[0] = 0xff;
        else
            data[0] = 0x00;
        data[1] = (byte)(Math.Abs(Tectemp) & 0xff);
        Set_ccd_tectmp_packet(data);

        Data_Packeted_Send();

        return Do_Waiting_Receive_Ok();
    }

    /****************************************************************
   * 函数名称: Read_Calibration_Data
   * 功能描述: 读取当前TEC温度值
   * 输入参数: 
   * 输出参数:  
   * 全局变量: none                                                    
   * 调用模块:     
   * 作　  者: 
   * 创建日期: 2016年03月17日
   * 修改日志：
   *****************************************************************/
    public Tuple<bool, string> Read_Tec_Tmp()
    {
        byte[] data = new byte[1];
        byte[] tecdata = new byte[5];
        sdatapacket.Clear();

        Sdata_head();
        Sdata_len(5);
        Sdata_cmd(rtectmp_cmd);
        data[0] = 0;
        Sdata_content(data);
        Sdata_checksum();

        Data_Packeted_Send();

        if (!Do_Waiting_Receive_Ok())
            return Tuple.Create(false, (string)null);

        for (int i = 0; i < rdata.Count; i++)
        {
            tecdata[i] = rdata[i];
        }

        var tectmp = System.Text.Encoding.ASCII.GetString(tecdata);

        return Tuple.Create(true, tectmp);
    }

    /****************************************************************
    * 函数名称: Load_Calibration_Data
    * 功能描述: 下载定标数据
    * 输入参数: 
    * 输出参数:  
    * 全局变量: none                                                    
    * 调用模块:     
    * 作　  者: 
    * 创建日期: 2016年03月16日
    * 修改日志：
    *****************************************************************/
    public bool Load_Calibration_Data(byte[] data)
    {

        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + data.Length);
        Sdata_cmd(LOAD_Calibration_DATA);
        Sdata_content(data);
        Sdata_checksum();

        Data_Packeted_Send();

        return Do_Waiting_Receive_Ok();
    }

    /****************************************************************
    * 函数名称: Load_Calibration_Data_PSNM
    * 功能描述: 下载定标数据
    * 输入参数: 
    * 输出参数:  
    * 全局变量: none                                                    
    * 调用模块:     
    * 作　  者: 
    * 创建日期: 2016年03月16日
    * 修改日志：
    *****************************************************************/
    public bool Load_Calibration_Data_PSNM(byte[] data)
    {

        sdatapacket.Clear();
        Sdata_head();
        Sdata_len(4 + data.Length);
        Sdata_cmd(LOAD_Calibration_DATA_PSNM);
        Sdata_content(data);
        Sdata_checksum();

        Data_Packeted_Send();

        return Do_Waiting_Receive_Ok();
    }

    /****************************************************************
    * 函数名称: Read_Calibration_Data
    * 功能描述: 读取定标数据
    * 输入参数: 
    * 输出参数:  
    * 全局变量: none                                                    
    * 调用模块:     
    * 作　  者: 
    * 创建日期: 2016年03月16日
    * 修改日志：
    *****************************************************************/
    public bool Read_Calibration_Data(byte[] data, byte[] refdata)
    {

        sdatapacket.Clear();

        Sdata_head();
        Sdata_len(5);
        Sdata_cmd(READ_Calibration_DATA);

        Sdata_content(data);
        Sdata_checksum();

        Data_Packeted_Send();

        if (!Do_Waiting_Receive_Ok())
            return false;
        for (int i = 0; i < rdata.Count - 2; i++)
        {
            refdata[i] = rdata[i + 2];
        }

        return true;
    }

    public bool Read_Ccd_Data_Atime(double[] refdata, bool checkstatus, byte mode)
    {
        try
        {
            //read dark data
            if (checkstatus == true)
            {
                Read_dark_Data_Atime();
            }

            Thread.Sleep(30);

            Start_ccd_scan_packet(mode);
            Data_Packeted_Send();
            if (!Do_Waiting_Receive_Ok())
                return false;
            //Thread.Sleep(Expose_Time*15/10);
            Read_ccd_data_packet();
            Data_Packeted_Send();

            Thread.Sleep(50);
            if (!Do_Waiting_Receive_Ok())
                return false;
            rdata.RemoveAt(0);
            if (checkstatus == true)
            {
                for (int i = 0; i < rdata.Count; i += 2)
                {
                    int a = rdata[i] - rdarkdata[i];
                    int b = rdata[i + 1] - rdarkdata[i + 1];
                    int k = i / 2;
                    refdata[k] = a * 256 + b;

                    a = rdarkdata[i];
                    b = rdarkdata[i + 1];
                    KDark[k] = a * 256 + b;
                    a = rdata[i];
                    b = rdata[i + 1];
                    KRaw[k] = a * 256 + b;
                }
            }
            else
            {

                for (int i = 0; i < rdata.Count; i += 2)
                {
                    int a = rdata[i];
                    int b = rdata[i + 1];

                    int k = i / 2;
                    refdata[k] = a * 256 + b;
                    a = rdata[i];
                    b = rdata[i + 1];
                    KRaw[k] = a * 256 + b;
                }

            }
            return true;
        }
        catch (Exception ex)
        {
            for (int i = 0; i < refdata.Length; i++)
                refdata[i] = 0;

            Array.Clear(KRaw, 0, KRaw.Length);
            Array.Clear(KDark, 0, KDark.Length);

            return false;
        }
    }

    public void Read_Raw_Data_To_Show(ref double[] refdata)
    {
        for (int k = 0; k < rdata.Count; k += 2)
        {
            int a = rdata[k];
            int b = rdata[k + 1];
            int u = k / 2;
            refdata[u] = a * 256 + b;
        }
        boxProcess(refdata);
    }

    public void Read_Dark_Data_To_Show(ref double[] refdata)
    {
        for (int k = 0; k < rdarkdata.Count; k += 2)
        {
            int a = rdarkdata[k];
            int b = rdarkdata[k + 1];
            int u = k / 2;
            refdata[u] = a * 256 + b;
        }
        boxProcess(refdata);
    }

    public bool Read_dark_Data_Atime()
    {
        rdarkdata.Clear();
        Generate_packet_bycmd(rccddark_cmd);
        Data_Packeted_Send();

        if (!Do_Waiting_Receive_Ok())
            return false;

        Read_ccd_data_packet();

        Data_Packeted_Send();

        Thread.Sleep(100);

        if (!Do_Waiting_Receive_Ok())
            return false;

        for (int i = 0; i < rdata.Count; i++)
        {
            rdarkdata.Add(rdata[i]);
        }
        rdarkdata.RemoveAt(0);

        return true;
    }

    public bool Read_dark_Data(double[] refdata)
    {
        rdarkdata.Clear();
        double[] ccddatatmp = new double[CCD_DATA_PACK_SIZE];
        for (int i = 0; i < refdata.Length; i++)
        {
            refdata[i] = 0;
        }

        if (DataPacketedService.Expose_Time > integraltime_add)
        {
            integraltime_num = DataPacketedService.Expose_Time / integraltime_add;
            integraltime_yushu = DataPacketedService.Expose_Time % integraltime_add;
        }

        for (int i = 0; i < Scan_time; i++)
        {

            for (int j = 0; j < integraltime_num - 1; j++)
            {

                bool result = Read_dark_Data_Atime();
                if (!result)
                    return false;

                for (int k = 0; k < rdarkdata.Count; k += 2)
                {
                    int a = rdarkdata[k];
                    int b = rdarkdata[k + 1];
                    int u = k / 2;
                    ccddatatmp[u] = a * 256 + b;
                }

                for (int l = 0; l < refdata.Length; l++)
                    refdata[l] += ccddatatmp[l];
            }

            ////////////////最后这次，如果有余数，直接设置余数值+10000ms////////////////////

            int time_tmp = DataPacketedService.Expose_Time;
            if (DataPacketedService.Expose_Time > integraltime_add)
            {
                DataPacketedService.Expose_Time = integraltime_yushu + integraltime_add;
            }
            Set_Exposed_Time();

            Thread.Sleep(20);

            DataPacketedService.Expose_Time = time_tmp;          //
            bool result1 = Read_dark_Data_Atime();
            if (!result1)
                return false;

            for (int k = 0; k < rdarkdata.Count; k += 2)
            {
                int a = rdarkdata[k];
                int b = rdarkdata[k + 1];
                int u = k / 2;
                ccddatatmp[u] = a * 256 + b;
            }

            for (int j = 0; j < refdata.Length && j < ccddatatmp.Length; j++)
                refdata[j] += ccddatatmp[j];
        }
        for (int i = 0; i < refdata.Length; i++)
        {
            refdata[i] = refdata[i] / Scan_time;
        }
        boxProcess(refdata);
        return true;

    }

    public bool Read_Ccd_Data(double[] refdata, bool darkcheckstatus, byte mode)
    {
        int lenght = 0;
        double[] ccddatatmp = new double[CCD_DATA_PACK_SIZE];

        double[] kdark = new double[CCD_DATA_PACK_SIZE];
        double[] kraw = new double[CCD_DATA_PACK_SIZE];
        for (int i = 0; i < refdata.Length; i++)
        {
            refdata[i] = 0;
        }

        if (DataPacketedService.Expose_Time > integraltime_add)
        {
            integraltime_num = DataPacketedService.Expose_Time / integraltime_add;
            integraltime_yushu = DataPacketedService.Expose_Time % integraltime_add;
        }


        for (int i = 0; i < Scan_time; i++)
        {
            for (int j = 0; j < integraltime_num - 1; j++)
            {
                bool result = Read_Ccd_Data_Atime(ccddatatmp, darkcheckstatus, mode);
                if (!result)
                    return false;


                if (ccddatatmp.Length > refdata.Length)
                    lenght = refdata.Length;
                else
                    lenght = ccddatatmp.Length;
                for (int k = 0; k < lenght; k++)
                    refdata[k] += ccddatatmp[k];

                /////////////////////保存平均次数累加的暗光谱与未扣暗光谱////////////
                if (kdark.Length > KDark.Length)
                    lenght = KDark.Length;
                else
                    lenght = kdark.Length;
                for (int k = 0; k < lenght; k++)
                    kdark[k] += KDark[k];

                if (kraw.Length > KRaw.Length)
                    lenght = KRaw.Length;
                else
                    lenght = kraw.Length;
                for (int k = 0; k < lenght; k++)
                    kraw[k] += KRaw[k];

                //////////////////////


            }

            ////////////////最后这次，如果有余数，直接设置余数值+10000ms////////////////////

            int time_tmp = DataPacketedService.Expose_Time;
            if (DataPacketedService.Expose_Time > integraltime_add)
            {
                DataPacketedService.Expose_Time = integraltime_yushu + integraltime_add;
            }

            Set_Exposed_Time();

            DataPacketedService.Expose_Time = time_tmp;          //

            Thread.Sleep(20);

            bool result1 = Read_Ccd_Data_Atime(ccddatatmp, darkcheckstatus, mode);
            if (!result1)
                return false;

            if (ccddatatmp.Length > refdata.Length)
                lenght = refdata.Length;
            else
                lenght = ccddatatmp.Length;
            for (int k = 0; k < refdata.Length; k++)
                refdata[k] += ccddatatmp[k];
            ////////////////////////////////////////////////////////////////////////////////////
            /////////////////////保存平均次数累加的暗光谱与未扣暗光谱////////////
            if (kdark.Length > KDark.Length)
                lenght = KDark.Length;
            else
                lenght = kdark.Length;
            for (int k = 0; k < lenght; k++)
                kdark[k] += KDark[k];

            if (kraw.Length > KRaw.Length)
                lenght = KRaw.Length;
            else
                lenght = kraw.Length;
            for (int k = 0; k < lenght; k++)
                kraw[k] += KRaw[k];

            //////////////////////



        }
        for (int i = 0; i < refdata.Length; i++)
        {
            refdata[i] = refdata[i] / Scan_time;
        }
        for (int i = 0; i < kdark.Length; i++)
        {
            kdark[i] = kdark[i] / Scan_time;
            KDark[i] = kdark[i];
        }
        for (int i = 0; i < kraw.Length; i++)
        {
            kraw[i] = kraw[i] / Scan_time;
            KRaw[i] = kraw[i];
        }

        boxProcess(KDark);
        boxProcess(KRaw);
        boxProcess(refdata);
        return true;

    }

    /// <summary>
    /// 设置平均次数
    /// </summary>
    /// <param name="value"></param>
    public void SetScanTime(int value)
    {
        if (value < 1)
        {
            throw new ArgumentException($"scan time value {value} invalid!");
        }

        Scan_time = value;
    }

    /// <summary>
    /// 设置平滑次数
    /// </summary>
    /// <param name="value">0:off 1:on</param>
    public void SetBoxWidth(int value)
    {
        if (value != 0 && value != 1 && value != 2)
            throw new ArgumentException();

        nBoxWidth = value;
    }

    /// <summary>
    /// 对采集到的当前数据帧内的像素值做加权平均
    /// </summary>
    /// <param name="refdata"></param>
    private void boxProcess(double[] refdata)
    {
        int smooth_level = 0;
        if (nBoxWidth == 0)
            return;
        if (nBoxWidth == 1)
        {
            smooth_level = 4;
        }
        if (nBoxWidth == 2)
        {
            smooth_level = 5;
        }
        int i, j, k;
        double sum;

        for (i = 0; i < smooth_level; i++)
        {
            sum = 0;
            k = Math.Min(i + smooth_level + 1, refdata.Length);
            for (j = 0; j < k; j++)
            {
                sum += refdata[j];
            }
            refdata[i] = sum / k;
        }

        k = 2 * smooth_level + 1;
        for (i = smooth_level; i < refdata.Length - smooth_level; i++)
        {
            sum = 0;
            for (j = i - smooth_level; j < i + smooth_level + 1; j++)
            {
                sum += refdata[j];
            }
            refdata[i] = sum / k;
        }

        for (i = refdata.Length - smooth_level; i < refdata.Length; i++)
        {
            sum = 0;
            k = smooth_level + CCD_DATA_PACK_SIZE - i;
            for (j = i - smooth_level; j < refdata.Length; j++)
            {
                sum += refdata[j];
            }
            refdata[i] = sum / k;
        }

    }
}

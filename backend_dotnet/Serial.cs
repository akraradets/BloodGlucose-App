using backend_dotnet.Utils;
using Grpc.Core;
using System.IO.Ports;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace backend_dotnet;

class Serial
{
    #region byte command
    private static byte rtemperture_cmd = 0x01;
    private static byte rmodulever_cmd = 0x02;
    private static byte rpn_cmd = 0x03;
    private static byte rsn_cmd = 0x04;
    private static byte wsn_cmd = 0x05;
    private static byte rdate_cmd = 0x06;
    private static byte rinf_cmd = 0x07;
    private static byte wdate_cmd = 0x08;
    private static byte rman_cmd = 0x09;
    private static byte rvol_cmd = 0x10;
    private static byte wtectmp_cmd = 0x12;
    private static byte rtectmp_cmd = 0x13;
    private static byte wccdexpo_cmd = 0x14;
    private static byte wccdscan_cmd = 0x16;
    private static byte rccddata_cmd = 0x17;
    private static byte wldcurrent_cmd = 0x20;
    private static byte rccddark_cmd = 0x23;
    private static byte rteclock_cmd = 0x19;
    #endregion

    #region config
    public int EXPOSE_TIME = 1000;
    public readonly int CCD_DATA_PACK_SIZE = 3648;
    public static int nBoxWidth = 0;
    public static string Smooth_Level = "NONE";
    #endregion

    private SerialPort _serial_port;
    private List<Byte> bytes_tosend       = new List<Byte>();
    public static List<Byte> bytes_toread = new List<Byte>();
    public static List<Byte> bytes_toread_dark = new List<Byte>();

    private readonly ILogger<RamanDevice> _logger;
    public Serial(ILogger<RamanDevice> logger, SerialPort serialport)
    {
        _logger = logger;
        _serial_port = serialport;
    }

    #region Serial
    private void serial_send()
    {
        byte[] bytes_array = bytes_tosend.ToArray();
        //try
        //{
        _serial_port.Write(bytes_array, 0, bytes_array.Length);
        //}
        //catch (Exception ex)
        //{
        //    Is_Port_Open = false;
        //}
    }
    private bool serial_read(List<byte> buffer)
    {
        // data will be store in buffer once command is sent.
        // we read the first 4 buffer size to know how the expected_size
        // then keep reading until the buffer_size reaches the expected_size
        // 
        // by default, Thread.Sleep(200) and 4 loops is sufficient to do this task.
        // However, if we want to tune this process faster.
        // Reduce the sleep time and increase the loop. (check more frequently)
        int buffer_size = 0;
        _logger.LogDebug("serial_read: start");
        for (int i = 0; i < 4; i++)
        {
            _logger.LogDebug($"serial_read: read round {i}");
            
            if (i > 0)
                Thread.Sleep(200);

            int size = _serial_port.BytesToRead;
            byte[] readBuffer = new byte[size];
            _serial_port.Read(readBuffer, 0, size);
            buffer_size += size;
            _logger.LogDebug($"serial_read: read size {size}, buffer_size={buffer_size}");
            buffer.AddRange(readBuffer);
            if (buffer_size >= 4)
            {
                int expected_size = (buffer[2] << 8) + buffer[3] + 2;
                _logger.LogDebug($"serial_read: buffer exceed 4. buffer[0]={buffer[0]} buffer[1]={buffer[1]} expected_size={expected_size} buffer_size={buffer_size}");
                if (buffer[0] == 0xaa && buffer[1] == 0x55 && expected_size == buffer_size)
                {
                    _logger.LogDebug($"serial_read: SUCCESS with CASE A");
                    buffer_size = 0;
                    return true;
                }
                else if (buffer[4] == 0x06 || buffer[4] == 0x04)
                {
                    _logger.LogDebug($"serial_read: SUCCESS with CASE B");
                    buffer_size = 0;
                    return true;
                }
                _logger.LogDebug($"serial_read: JUST MOVE ON???????");
            }
        }
        buffer_size = 0;
        _logger.LogError($"serial_read: Ah. Fail to read!");
        return false;
    }

    #endregion

    #region CMD Block
    private void block_clear()
    {
        bytes_tosend.Clear();
    }
    private void block_head()
    {
        bytes_tosend.Add((byte)0xaa);
        bytes_tosend.Add((byte)0x55);
    }
    private void block_len(int len)
    {
        bytes_tosend.Add((byte)((len >> 8) & 0xff));
        bytes_tosend.Add((byte)((len) & 0xff));
    }
    private void block_cmd(byte cmd)
    {
        bytes_tosend.Add(cmd);
    }
    private void block_content(byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            bytes_tosend.Add(data[i]);
        }
    }
    private void block_checksum()
    {
        int check_sum = 0;
        for (int i = 2; i < bytes_tosend.Count; i++)
        {

            check_sum += bytes_tosend[i];
            check_sum &= 0xff;
        }
        bytes_tosend.Add((byte)check_sum);
    }
    #endregion


    #region Create Command
    private bool cmd(byte cmd)
    {
        block_clear();
        block_head();
        block_len(4);
        block_cmd(cmd);
        block_checksum();
        serial_send();
        return is_read_ok();
    }
    private bool cmd_start_ccd_scan_packet(byte mode)
    {
        block_clear();
        byte[] data = new byte[2];
        block_head();
        block_len(6);
        block_cmd(wccdscan_cmd);
        data[0] = mode;
        data[1] = 0x01;
        block_content(data);
        block_checksum();
        serial_send();
        return is_read_ok();
    }

    private bool cmd_ccd_data_read()
    {
        block_clear();
        byte[] data = new byte[1];
        block_head();
        block_len(5);
        block_cmd(rccddata_cmd);
        data[0] = 0x00;
        block_content(data);
        block_checksum();
        serial_send();
        return is_read_ok();
    }
    private bool cmd_set_laser_power(byte[] data)
    {
        block_clear();
        block_head();
        block_len(4 + data.Length);
        block_cmd(wldcurrent_cmd);
        block_content(data);
        block_checksum();
        serial_send();
        return is_read_ok();
    }
    private bool cmd_set_exposure(byte[] data)
    {
        block_clear();
        block_head();
        block_len(4 + data.Length);
        block_cmd(wccdexpo_cmd);
        block_content(data);
        block_checksum();
        serial_send();
        return is_read_ok();
    }

    private bool cmd_set_temperature(byte[] data)
    {
        block_clear();
        block_head();
        block_len(4 + data.Length);
        block_cmd(wtectmp_cmd);
        block_content(data);
        block_checksum();
        serial_send();
        return is_read_ok();
    }

    private bool cmd_get_temperature(byte[] data)
    {
        block_clear();
        block_head();
        block_len(5);
        block_cmd(rtectmp_cmd);
        data[0] = 0;
        block_content(data);
        block_checksum();
        serial_send();
        return is_read_ok();
    }

    #endregion


    #region READ DATA helper
    private bool _read_data__is_data_here()
    {
        //Have_Receive_Data
        _logger.LogDebug("_read_data__is_data_here: start");
        int checksum = 0;
        for (int i = 2; i < bytes_toread.Count - 1; i++)
        {
            checksum += bytes_toread[i];
            checksum &= 0xff;
        }

        if (checksum == bytes_toread[bytes_toread.Count - 1])
        {
            bytes_toread.RemoveAt(0);
            bytes_toread.RemoveAt(0);
            bytes_toread.RemoveAt(bytes_toread.Count - 1);
            _logger.LogDebug("_read_data__is_data_here: HERE");
            return true;
        }
        else
        {
            _logger.LogDebug("_read_data__is_data_here: NOT HERE");
            return false;
        }
    }
    private int _read_data__is_valid()
    {
        //Check_Receive_Data
        _logger.LogDebug("_read_data__is_valid: start");
        int len = (bytes_toread[0] << 8) + bytes_toread[1] - 2;
        if (len < 3)
        {
            _logger.LogDebug("_read_data__is_valid: len < 3");
            return 1;
        }
        bytes_toread.RemoveAt(0);
        bytes_toread.RemoveAt(0);
        byte cmd = bytes_toread[0];
        bytes_toread.RemoveAt(0);
        byte response = bytes_toread[0];
        _logger.LogDebug($"_read_data__is_valid: response={response}");
        if (response == 0x00)
            return 0;
        else if (response == 0xff)
            return 0xff;
        else if (response == 0x2d || response == 0x30)
            return 0;
        else
            return 0;
    }

    #endregion

    #region Function
    private bool _read_data()
    {
        bytes_toread.Clear();

        // try read
        bool result = serial_read(bytes_toread);
        if (result == false)
        {
            _logger.LogInformation("_read_data: Failed");
            return false;
        }
        // check data is received
        return _read_data__is_data_here();
    }

    private bool is_read_ok()
    {
        _logger.LogDebug("is_read_ok: start");
        //if (SerialService.Is_Port_Open == false)
        //    throw new RpcException(new Status(StatusCode.FailedPrecondition, "Port is not open"));

        var eta = DateTime.Now.AddMilliseconds(EXPOSE_TIME + 3000);
        _logger.LogDebug($"is_read_ok: eta={eta} with EXPOSE_TIME={EXPOSE_TIME}");
        int try_count = 0, countmax = 0;
        bool do_retry = false;
        while (true)
        {
            if (!_read_data())
            {
                _logger.LogError($"is_read_ok: Can not read data. Do retry");
                do_retry = true;
            }
            else
            {
                // We got the data!!! Let's check the data.
                switch (_read_data__is_valid())
                {
                    case 1:
                        _logger.LogError($"is_read_ok: BAD DATA READ: len < 3");
                        return false;
                    case 0xff:
                        _logger.LogDebug($"is_read_ok: BAD DATA READ: because corrupted data");
                        do_retry = true;
                        break;
                    default:
                        _logger.LogDebug($"is_read_ok: READ OK!!");
                        return true;
                }
            }

            if (do_retry)
            {
                do_retry = false;
                //Thread.Sleep(200);
                serial_send();
                try_count++;
                countmax = EXPOSE_TIME / 50 + 550;
                _logger.LogDebug($"is_read_ok: try_count={try_count} count_max={countmax} datetime={DateTime.Now}");
                if (try_count < countmax && DateTime.Now <= eta)
                {
                    _logger.LogDebug($"is_read_ok: try again");
                    continue;
                }
                else
                {
                    _logger.LogError($"is_read_ok: retry exceeded");
                    return false;
                }
            }
        }
    }

    #endregion

    #region Public Interface

    public double[] read_dark_ccd()
    {
        //Read_dark_Data_Atime
        double[] refdata = new double[CCD_DATA_PACK_SIZE];
        for (int i = 0; i < refdata.Length; i++)
        {
            refdata[i] = 0;
        }
        _logger.LogDebug("read_dark_ccd: calling `cmd(rccddark_cmd)`");
        bytes_toread_dark.Clear();
        if (!cmd(rccddark_cmd))
        {
            throw new RpcException((new Status(StatusCode.Internal, "cmd(rccddark_cmd) failed")));
        }

        //Thread.Sleep(Expose_Time*15/10);
        _logger.LogDebug("read_read_dark_ccd: calling `cmd_ccd_data_read`");
        if (!cmd_ccd_data_read())
            throw new RpcException((new Status(StatusCode.Internal, "ccd_data_read failed")));


        for(int i = 0; i < bytes_toread.Count; i++)
        {
            bytes_toread_dark.Add(bytes_toread[i]);
        }
        bytes_toread_dark.RemoveAt(0);

        for (int k = 0; k < bytes_toread_dark.Count; k += 2)
        {
            int a = bytes_toread_dark[k];
            int b = bytes_toread_dark[k + 1];
            int u = k / 2;
            refdata[u] = a * 256 + b;
        }
        //string msg = "";
        //for (int i = 0; i < bytes_toread.Count; i += 2)
        //{
        //    int a = bytes_toread[i];
        //    int b = bytes_toread[i + 1];

        //    int k = i / 2;
        //    refdata[k] = a * 256 + b;
        //    a = bytes_toread[i];
        //    b = bytes_toread[i + 1];
        //    msg += $"{k}:{a * 256 + b} ";
        //    //KRaw[k] = a * 256 + b;

        //}
        //bytes_toread.Clear();
        //_logger.LogDebug($"read_read_dark_ccd: {msg}");
        boxProcess(refdata);
        return refdata;
    }

    public double[] read_ccd(byte mode)
    {
        //Read_Ccd_Data

        //if (IS_ARMED == false)
        //    throw new RpcException(new Status(StatusCode.FailedPrecondition, "Forget to arm the read_ccd"));

        double[] refdata = new double[CCD_DATA_PACK_SIZE];
        for (int i = 0; i < refdata.Length; i++)
        {
            refdata[i] = 0;
        }
        //Read_Ccd_Data_Atime
        _logger.LogDebug("read_ccd: calling `cmd_start_ccd_scan_packet`");
        if (!cmd_start_ccd_scan_packet(mode))
            throw new RpcException((new Status(StatusCode.Internal, "start_scan_packet failed")));


        //Thread.Sleep(Expose_Time*15/10);
        _logger.LogDebug("read_ccd: calling `cmd_ccd_data_read`");
        if (!cmd_ccd_data_read())
            throw new RpcException((new Status(StatusCode.Internal, "ccd_data_read failed")));


        bytes_toread.RemoveAt(0);
        string msg = "";
        for (int i = 0; i < bytes_toread.Count; i += 2)
        {
            int a = bytes_toread[i];
            int b = bytes_toread[i + 1];

            int k = i / 2;
            refdata[k] = a * 256 + b;
            //a = bytes_toread[i];
            //b = bytes_toread[i + 1];
            //msg += $"{k}:{a * 256 + b} ";
            //KRaw[k] = a * 256 + b;

        }
        //bytes_toread.Clear();
        //_logger.LogDebug($"read_ccd: {msg}");
        boxProcess(refdata);
        return refdata;
    }

    public bool set_laser_power(int laser_power)
    {
        byte[] data = new byte[2];
        data[0] = (byte)((laser_power >> 8) & 0xff);
        data[1] = (byte)(laser_power & 0xff);
        bool result = cmd_set_laser_power(data);
        return result;
    }

    public bool set_exposure(int exposure)
    {
        byte[] data = new byte[2];
        data[0] = (byte)((exposure >> 8) & 0xff);
        data[1] = (byte)(exposure & 0xff);
        bool result = cmd_set_exposure(data);
        if (result)
        {
            EXPOSE_TIME = exposure;
        }
        return result;
    }
    #endregion

    public bool set_temperature(int temperature)
    {
        byte[] data = new byte[2];
        if (temperature < 0)
            data[0] = 0xff;
        else
            data[0] = 0x00;
        data[1] = (byte)(Math.Abs(temperature) & 0xff);
        bool result = cmd_set_temperature(data);
        return result;
    }

    public string get_temperature()
    {
        byte[] data = new byte[1];
        byte[] tecdata = new byte[5];
        bool result = cmd_get_temperature(data);
        if (result)
        {
            for(int i = 0; i < bytes_toread.Count; i++)
            {
                tecdata[i] = bytes_toread[i];
                Console.WriteLine($"============================== TEMP[{i}] = {bytes_toread[i]}");
            }
            //for (int i = 0; i < bytes_toread.Count; i += 2)
            //{
            //    int a = bytes_toread[i];
            //    int b = bytes_toread[i + 1];

            //    int k = i / 2;
            //    tecdata[k] = a * 256 + b;
            //a = bytes_toread[i];
            //b = bytes_toread[i + 1];
            //msg += $"{k}:{a * 256 + b} ";
            //KRaw[k] = a * 256 + b;

            //}
            string temp = System.Text.Encoding.ASCII.GetString(tecdata);
            Console.WriteLine($"============================== TEMP = {temp}");
            return temp;
        }
        throw new SystemException($"Fail to read CCD temp");
    }


    #region Signal Processing
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

    #endregion
}
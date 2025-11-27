using backend_dotnet.Model;
using backend_dotnet.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace backend_dotnet.Utils;

public class ATRWrapper
{
    #region Property

    private static SerialService serial_ops;

    private static DataPacketedService data_packet_ops;

    private static WavenumCal _wavenumCal;

    private static int CCD_DATA_PACK_SIZE = 3648;

    private static double[] _data = new double[3648];

    private static double[] _dark;

    private static MicroRaman _microRaman { get; set; } = new MicroRaman();

    /// <summary>
    /// mimimum cool temperature
    /// </summary>
    private int _minCoolTemperature = -5;

    /// <summary>
    /// maximum cool temperature
    /// </summary>
    private int _maxCoolTemperature = 25;

    #endregion

    static ATRWrapper()
    {
        serial_ops = new SerialService();
        data_packet_ops = new DataPacketedService(serial_ops);
        _wavenumCal = new WavenumCal(data_packet_ops);
    }

    #region Open/Close Device

    public bool OpenDevice(string serialportname)
    {
        try
        {
            if (serial_ops.Get_Device_Status())
                return true;

            //string serialportname = serial_ops.WhoUsbDevice(0x0483, 0x5740);

            //if (serialportname == null) return false;

            bool result = serial_ops.Serial_Port_Open(serialportname);

            if (!Init()) return false;

            return result;
        }
        catch
        {
            return false;
        }
    }

    private bool Init()
    {
        string modulesn = "";

        var result = data_packet_ops.Read_ModuleSN();

        if (!result.Item1) return false;

        modulesn = result.Item2;
        modulesn = modulesn.Substring(0, 7);

        if (modulesn == "ATP5020")
        {
            CCD_DATA_PACK_SIZE = 2048;
        }
        else if (modulesn == "ATR3000")
        {
            CCD_DATA_PACK_SIZE = 2048;
        }
        else if (modulesn == "ATR2000")
        {
            CCD_DATA_PACK_SIZE = 3648;
        }
        else if (modulesn == "ATR8217" || modulesn == "ATP8217")
        {
            CCD_DATA_PACK_SIZE = 512;
        }
        else if (modulesn == "ATR6500" || modulesn == "ATP6500")
        {
            CCD_DATA_PACK_SIZE = 1024;
        }
        else
        {
            CCD_DATA_PACK_SIZE = 3648;
        }

        if (!_wavenumCal.Calibrate()) return false;

        _wavenumCal.CalWaveNum(CCD_DATA_PACK_SIZE);

        return true;
    }

    public bool CloseDevice()
    {
        try
        {
            if (serial_ops.Get_Device_Status())
            {
                CleanUp();

                serial_ops.Serial_Port_Close();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void CleanUp()
    {
        _microRaman = new MicroRaman();

        // turn off laser power
        SetLdPower(0);
    }

    #endregion

    #region Device property

    /// <summary>
    /// 获取SN号
    /// </summary>
    /// <returns></returns>
    public string GetSn()
    {
        if (!serial_ops.Get_Device_Status())
            return null;

        var res = data_packet_ops.Read_ModuleSN();

        if (res.Item1) return res.Item2;
        else return null;
    }

    /// <summary>
    /// get raman shift
    /// </summary>
    /// <returns></returns>
    public double[] GetWaveNum()
    {
        return WavenumCal._xwavelen.ToArray();
    }

    /// <summary>
    ///get save str
    /// </summary>
    /// <returns></returns>        
    public string GetSaveStrData()
    {
        var sb = new StringBuilder();

        sb.AppendLine("Pixel;Wavenum;Value");

        for (var i = 0; i < CCD_DATA_PACK_SIZE; i++)
        {
            sb.AppendLine(i + ";" + WavenumCal._xwavelen[i].ToString("f4") + ";" + _data[i].ToString("f4"));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 获取像素个数
    /// </summary>
    /// <returns></returns>
    public int GetCCDSize()
    {
        return CCD_DATA_PACK_SIZE;
    }

    #endregion

    #region Acquire relate method

    /// <summary>
    /// 设置积分时间
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public bool SetIntegrationTime(int value)
    {
        if (!serial_ops.Get_Device_Status())
            return false;

        DataPacketedService.Expose_Time = value;

        if (value > DataPacketedService.integraltime_add)
            return false;

        var result = data_packet_ops.Set_Exposed_Time();  //按10s累加，小于10s直接设置

        return result;
    }

    /// <summary>
    /// 采集光谱
    /// </summary>
    /// <returns></returns>
    public Spectrum AcquireSpectrum(AcquireMethod acquireMethod = AcquireMethod.Precision)
    {
        if (!serial_ops.Get_Device_Status())
            return null;

        if (acquireMethod != AcquireMethod.Quick)
        {
            return AcquireSpectrumPrecision(acquireMethod);
        }
        else
        {
            return AcquireSpectrumQuick();
        }
    }

    /// <summary>
    /// 采集光谱快速模式
    /// </summary>
    /// <returns></returns>
    private Spectrum AcquireSpectrumQuick()
    {
        if (!serial_ops.Get_Device_Status())
            return null;

        // 更新设备激光功率[注：整机请开启该功能，非整机请不要开启该功能]
        //var ldPowerRes = InitProbe(50); 
        byte mode = 0;
        var checkstatus = false;

        var ccdnow = new double[CCD_DATA_PACK_SIZE];

        // 精确采集
        var result = data_packet_ops.Read_Ccd_Data(ccdnow, checkstatus, mode);

        var res = new Spectrum()
        {
            Data = ccdnow
        };

        if (result == false)
            res.Success = false;
        else
        {
            res.Success = true;

            for (var i = 0; i < res.Data.Length && _dark != null && i < _dark.Length; i++)
                res.Data[i] -= _dark[i];
        }

        return res;
    }

    /// <summary>
    /// 更新设备激光功率  // 1.激光功率参数有变化更新设备激光功率 2.Keep laser on and laser power not equal probe power
    /// </summary>
    /// <param name="ldNum">激光功率</param>
    /// <returns></returns>
    private Tuple<bool, string> InitProbe(int ldNum)
    {

        DataPacketedService.Ldcurrent = ldNum;

        var result = data_packet_ops.Set_Ld_Current();

        if (!result)
            return Tuple.Create(false, "设置激光功率失败!");

        return Tuple.Create(true, string.Empty);
    }

    /// <summary>
    /// 采集光谱精确模式
    /// </summary>
    /// <param name="acquireMethod"></param>
    /// <returns></returns>
    private Spectrum AcquireSpectrumPrecision(AcquireMethod acquireMethod)
    {
        if (!serial_ops.Get_Device_Status())
            return null;
        // 更新设备激光功率[注：整机请开启该功能，非整机请不要开启该功能]
        Console.WriteLine("----------LASERRRRR!!!!: " + DataPacketedService.Ldcurrent);
        var ldPowerRes = InitProbe(DataPacketedService.Ldcurrent); 
        byte mode = 0;

        if (acquireMethod == AcquireMethod.HighPrecision)
            mode = 0x01;

        var checkstatus = true;

        var ccdnow = new double[CCD_DATA_PACK_SIZE];

        // 精确采集
        var result = data_packet_ops.Read_Ccd_Data(ccdnow, checkstatus, mode);

        var res = new Spectrum()
        {
            Data = ccdnow
        };

        if (result == false)
            res.Success = false;
        else
            res.Success = true;

        //_Data = ccdnow.ToArray();

        return res;
    }

    /// <summary>
    /// 采集背景光谱
    /// </summary>
    /// <returns></returns>
    public Spectrum AcquireDarkSpectrum()
    {
        if (!serial_ops.Get_Device_Status())
            return null;
        // 更新设备激光功率[注：整机请开启该功能，非整机请不要开启该功能]
        var ldPowerRes = InitProbe(0); 
        var darkdata = new double[CCD_DATA_PACK_SIZE];

        bool result = data_packet_ops.Read_dark_Data(darkdata);

        var res = new Spectrum()
        {
            Data = darkdata
        };

        if (result == false)
            res.Success = false;
        else
        {
            res.Success = true;

            _dark = new double[CCD_DATA_PACK_SIZE];
            for (var i = 0; i < darkdata.Length; i++)
                _dark[i] = darkdata[i];
        }

        return res;
    }

    private Spectrum AcquireSpectrumDummy()
    {
        var rn = new Random();

        var ccdnow = new double[CCD_DATA_PACK_SIZE];

        int i;
        for (i = 0; i < ccdnow.Length; i++)
        {
            var x = (double)i / 200.0 * Math.PI * 2.0;
            var y = Math.Sin(x);
            x = x + 1000;
            y = y + 1000;

            ccdnow[i] = Math.Sin(x) + 1000;
        }

        return new Spectrum()
        {
            Success = true,
            Data = ccdnow
        };
    }

    /// <summary>
    /// Set laser power
    /// </summary>
    /// <param name="value">power</param>
    /// <param name="laserType">laser type 0:532nm laser 1:785nm laser</param>
    /// <returns></returns>
    public bool SetLdPower(int value, byte laserType = 0)
    {
        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        if (laserType == 0)
        {
            if (value < 0 || value > 100)
                return false;
        }
        else if (laserType == 1)
        {
            if (value < 0 || value > 500)
                return false;
        }
        else if (value < 0 || value > 100)
            return false;

        if (!serial_ops.Get_Device_Status())
            return false;

        DataPacketedService.Ldcurrent = value;
        var result = data_packet_ops.Set_Ld_Current();
        return result;
    }

    public bool SetCool(int temperature)
    {
        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        if (temperature < _minCoolTemperature || temperature > _maxCoolTemperature)
            return false;

        DataPacketedService.Tectemp = temperature;

        var res = data_packet_ops.Set_Tec_Tmp();

        return res;
    }

    /// <summary>
    /// Get CCD temperature cool
    /// </summary>
    /// <returns></returns>
    public float GetCool()
    {
        var value = 0f;

        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        var res = data_packet_ops.Read_Tec_Tmp();

        if (!res.Item1)
            throw new Exception("Read CCD temperature fail.");

        if (!float.TryParse(res.Item2, out value))
            throw new Exception($"Parse CCD temperature {res.Item2} fail.");

        return value;
    }

    #endregion

    #region Algorithm

    public double[] BaseLineCorrect(double[] data)
    {
        double[] databuff = data.ToArray();

        var correctData = FittingFunct.NewBaselineCorrectionInt2(databuff, databuff.Length);

        return correctData;
    }

    /// <summary>
    /// smooth boxcar
    /// </summary>
    /// <param name="data"></param>
    /// <param name="winSize"></param>
    /// <returns></returns>
    public double[] SmoothBoxcar(double[] data, int winSize)
    {
        if (winSize < 1 || winSize > 100)
            throw new ArgumentException($"Invalid parameter {winSize} value.");

        var windowSize = winSize * 2 + 1;
        var signal_flted = new double[data.Length];

        var halfWinSize = (windowSize - 1) / 2;

        var signal_before = new double[data.Length + halfWinSize * 2];

        // 数据准备
        for (var i = 0; i < halfWinSize; i++)
        {
            signal_before[i] = data[halfWinSize - 1 - i];
            signal_before[data.Length + halfWinSize + i] = data[data.Length - 1 - i];
        }

        for (var i = 0; i < data.Length; i++)
        {
            signal_before[halfWinSize + i] = data[i];
        }

        // 强度计算
        for (var i = 0; i < data.Length; i++)
        {
            var sum_signal = 0d;

            for (var j = 0; j < windowSize; j++)
            {
                sum_signal += signal_before[i + j];
            }

            signal_flted[i] = sum_signal / windowSize;
        }

        return signal_flted;
    }

    #endregion

    #region micro raman motor/LED light

    public MotorMoveResult SetXMotorMove(XMotorDirection direction, int angle)
    {
        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        #region parameter check and limit check

        if (angle < 0 || angle > 360)
            throw new ArgumentException("Angle out of range [0-360].");

        if (direction == XMotorDirection.Forward && _microRaman.XState == MotorMoveResult.ReachForward)
            return MotorMoveResult.ReachForward;

        if (direction == XMotorDirection.Backward && _microRaman.XState == MotorMoveResult.ReachBackward)
            return MotorMoveResult.ReachBackward;

        #endregion

        // SetXMotor_Step
        var res = data_packet_ops.SetXMotor_Step((byte)direction, angle);

        if (res == 0)
        {
            _microRaman.XState = MotorMoveResult.Success;
            return MotorMoveResult.Success;
        }
        else if (res == 0x01)
        {
            _microRaman.XState = MotorMoveResult.ReachBackward;
            return MotorMoveResult.ReachBackward;
        }
        else if (res == 0x02)
        {
            _microRaman.XState = MotorMoveResult.ReachForward;
            return MotorMoveResult.ReachForward;
        }

        return MotorMoveResult.Fail;
    }

    /// <summary>
    /// legacy x motor
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="angle"></param>
    /// <returns></returns>
    public MotorMoveResult SetXMotorMoveLegacy(XMotorDirection direction, int angle)
    {
        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        if (angle < 0 || angle > 360)
            throw new ArgumentException("Angle out of range [0-360].");

        var res = data_packet_ops.SetXMotor_StepLegacy((byte)direction, angle);

        return MotorMoveResult.Success;
    }

    public MotorMoveResult SetYMotorMove(YMotorDirection direction, int angle)
    {
        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        #region Parameter check and limit check

        if (angle < 0 || angle > 360)
            throw new ArgumentException("Angle out of range [0-360].");

        if (direction == YMotorDirection.Left && _microRaman.YState == MotorMoveResult.ReachLeft)
            return MotorMoveResult.ReachLeft;

        if (direction == YMotorDirection.Right && _microRaman.YState == MotorMoveResult.ReachRight)
            return MotorMoveResult.ReachRight;

        #endregion

        var res = data_packet_ops.SetYMotor_Step((byte)direction, angle);

        if (res == 0)
        {
            _microRaman.YState = MotorMoveResult.Success;
            return MotorMoveResult.Success;
        }
        else if (res == 0x01)
        {
            _microRaman.YState = MotorMoveResult.ReachLeft;
            return MotorMoveResult.ReachLeft;
        }
        else if (res == 0x02)
        {
            _microRaman.YState = MotorMoveResult.ReachRight;
            return MotorMoveResult.ReachRight;
        }

        return MotorMoveResult.Fail;
    }

    /// <summary>
    /// legacy y motor
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="angle"></param>
    /// <returns></returns>
    public MotorMoveResult SetYMotorMoveLegacy(YMotorDirection direction, int angle)
    {
        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        if (angle < 0 || angle > 360)
            throw new ArgumentException("Angle out of range [0-360].");

        var res = data_packet_ops.SetYMotor_StepLegacy((byte)direction, angle);

        return MotorMoveResult.Success;
    }

    public MotorMoveResult SetZMotorMove(ZMotorDirection direction, int angle)
    {
        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        #region 参数验证和限位控制

        if (angle < 0 || angle > 360)
            throw new ArgumentException("Angle out of range [0-360].");

        if (_microRaman.ZState == MotorMoveResult.ReachTop && direction == ZMotorDirection.UP)
            return MotorMoveResult.ReachTop;

        if (_microRaman.ZState == MotorMoveResult.ReachBottom && direction == ZMotorDirection.Down)
            return MotorMoveResult.ReachBottom;

        #endregion

        var res = data_packet_ops.SetZMotor_Step((byte)direction, angle);

        #region 结果处理

        if (res == 0)
        {
            // 清除限位状态
            _microRaman.ZState = MotorMoveResult.Success;

            return MotorMoveResult.Success;
        }
        else if (res == 0x02)
        {
            _microRaman.ZState = MotorMoveResult.ReachTop;

            return MotorMoveResult.ReachTop;
        }
        else if (res == 0x01)
        {
            _microRaman.ZState = MotorMoveResult.ReachBottom;

            return MotorMoveResult.ReachBottom;
        }

        #endregion

        return MotorMoveResult.Fail;
    }

    public MotorMoveResult SetZMotorMoveLegacy(ZMotorDirection direction, int angle)
    {
        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        #region 参数验证和限位控制

        if (angle < 0 || angle > 360)
            throw new ArgumentException("Angle out of range [0-360].");

        #endregion

        var res = data_packet_ops.SetZMotor_Step((byte)direction, angle);

        #region 结果处理

        if (res == 0)
        {
            // 清除限位状态
            _microRaman.ZState = MotorMoveResult.Success;

            return MotorMoveResult.Success;
        }
        else if (res == 0x01 && direction == ZMotorDirection.Down)
        {
            _microRaman.ZState = MotorMoveResult.ReachBottom;

            // 回转
            for (var i = 0; i < 5; i++)
            {
                Thread.Sleep(100);
                var tmp = data_packet_ops.SetZMotor_Step((byte)ZMotorDirection.UP, 360);
            }

            return MotorMoveResult.ReachBottom;
        }
        else if (res == 0x01 && direction == ZMotorDirection.UP)
        {
            _microRaman.ZState = MotorMoveResult.ReachTop;

            // 回转
            for (var i = 0; i < 5; i++)
            {
                Thread.Sleep(100);
                var tmp = data_packet_ops.SetZMotor_Step((byte)ZMotorDirection.Down, 360);
            }

            return MotorMoveResult.ReachTop;
        }
        else if (res == 0x02 && direction == ZMotorDirection.UP)
        {
            _microRaman.ZState = MotorMoveResult.ReachTop;

            // 回转
            for (var i = 0; i < 5; i++)
            {
                Thread.Sleep(100);
                data_packet_ops.SetZMotor_Step((byte)ZMotorDirection.Down, 360);
            }

            return MotorMoveResult.ReachTop;
        }

        #endregion

        return MotorMoveResult.Fail;
    }

    /// <summary>
    /// 设置灯源电压
    /// </summary>
    /// <param name="voltage"></param>
    /// <returns></returns>
    public bool SetLightVoltage(int voltage)
    {
        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        if (voltage < 0 || voltage > 5)
            throw new ArgumentException($"voltage out of range [0-5].");

        var res = data_packet_ops.SetLightVoltage(voltage);

        return true;
    }

    /// <summary>
    /// 关闭电机
    /// </summary>
    /// <returns></returns>
    public bool TurnOffMotorLegacy()
    {
        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        if (!_microRaman.IsMotorTurnOn) return true;

        data_packet_ops.SetSwitchMotor_Legacy(0);
        Thread.Sleep(200);
        _microRaman.IsMotorTurnOn = false;

        return true;
    }

    /// <summary>
    /// 开启电机
    /// </summary>
    /// <returns></returns>
    public bool TurnOnMotorLegacy()
    {
        if (!serial_ops.Get_Device_Status())
            throw new ArgumentException("Device is closed!");

        if (_microRaman.IsMotorTurnOn)
        {
            return true; ;
        }

        data_packet_ops.SetSwitchMotor_Legacy(1);
        Thread.Sleep(200);
        _microRaman.IsMotorTurnOn = true;

        return true;
    }

    #endregion

    #region Raman Shift Calibration

    /// <summary>
    /// Raman shift calibration
    /// </summary>
    /// <param name="ramanShifts"></param>
    /// <returns></returns>
    public bool RamanShiftCalibrate(int[] pixels)
    {
        var res = _wavenumCal.RamanShiftCalibrate(pixels);

        if (res)
            _wavenumCal.CalWaveNum(CCD_DATA_PACK_SIZE);

        return res;
    }

    #endregion
}

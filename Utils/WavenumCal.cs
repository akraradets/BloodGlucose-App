using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace backend_dotnet.Utils;

internal class WavenumCal
{
    public static DataPacketedService _dataPacketedService;

    public WavenumCal(DataPacketedService dataPacketedService)
    {
        _dataPacketedService = dataPacketedService;
    }

    /// <summary>
    /// 波数
    /// </summary>
    public static double[] _xwavelen = new double[3648];

    /// <summary>
    /// 校正像素
    /// </summary>
    private static double[] _pixel = new double[8];

    /// <summary>
    /// 校正波数
    /// </summary>
    private static double[] _wavenum = new double[8];

    /// <summary>
    /// 校正系数
    /// </summary>
    private static double[] _coneff = new double[8];

    /// <summary>
    /// 校正峰数
    /// </summary>
    private static int nPeakNumber = 2;

    private void Load()
    {
        _pixel[0] = 29;
        _pixel[1] = 122;
        _pixel[2] = 209;
        _pixel[3] = 407;

        _wavenum[0] = 378;
        _wavenum[1] = 918;
        _wavenum[2] = 1374;
        _wavenum[3] = 2252;

        nPeakNumber = 4;

    }

    private string GetPath()
    {
        var path = new Uri(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase).LocalPath;

        return System.IO.Path.GetDirectoryName(path);
    }

    /// <summary>
    /// 计算默认的校正系数
    /// </summary>
    public bool Calibrate()
    {
        var res = false;

        Load();

        Thread.Sleep(200);

        res = Calibration_Data_Display();

        do_calibration();

        return res;
    }

    /// <summary>
    /// 函数名称: Calibration_Data_Dispaly
    /// 功能描述: 定标数据从光谱仪读取并显示
    /// 输入参数:
    /// 输出参数:
    /// 全局变量: none         
    /// 调用模块:     
    /// 作　  者: 
    /// 创建日期: 2016年03月16日
    /// 修改日志：
    /// </summary>
    /// <returns></returns>
    private bool Calibration_Data_Display()
    {
        if (nPeakNumber > 8) throw new InvalidOperationException("nPeakNumber great than 8!");

        byte[] data = new byte[1];

        bool result_status = false;

        data[0] = (byte)(nPeakNumber);

        byte[] buff = new byte[(nPeakNumber * 4)];

        result_status = _dataPacketedService.Read_Calibration_Data(data, buff);

        if (!result_status) return false;

        double[] nums = new double[nPeakNumber];

        for (var i = 0; i < nPeakNumber; i++)
        {
            var index = 4 * i;
            var tmpNum = (buff[index] << 24) + (buff[index + 1] << 16) + (buff[index + 2] << 8) + buff[index + 3];
            _pixel[i] = tmpNum;
        }

        return result_status;
    }

    private int do_calibration()
    {
        Gauss();

        return 0;
    }

    private void Gauss()
    {
        double d;
        int n = nPeakNumber;
        double[,] a = new double[n, n + 1];
        int i, j, k;
        for (i = 0; i < n; i++)
        {
            for (j = 0; j < n; j++)
            {
                a[i, j] = Math.Pow(_pixel[i], j);
            }
            a[i, n] = WaveNum_To_WaveLength(_wavenum[i]);
        }
        // 消元  
        for (k = 0; k < n; k++)
        {
            selectMainElement(n, k, a); // 选择主元素  

            // for (int j = k; j <= n; j++ ) a[k, j] = a[k, j] / a[k, k];  
            // 若将下面两个语句改为本语句，则程序会出错，因为经过第1次循环  
            // 后a[k,k]=1，a[k,k]的值发生了变化，所以在下面的语句中先用d  
            // 将a[k,k]的值保存下来  
            d = a[k, k];
            for (j = k; j <= n; j++) a[k, j] = a[k, j] / d;

            // Guass消去法与Jordan消去法的主要区别就是在这一步，Gauss消去法是从k+1  
            // 到n循环，而Jordan消去法是从1到n循环，中间跳过第k行  
            for (i = k + 1; i < n; i++)
            {
                d = a[i, k];  // 这里使用变量d将a[i,k]的值保存下来的原理与上面注释中说明的一样  
                for (j = k; j <= n; j++) a[i, j] = a[i, j] - d * a[k, j];
            }


        }

        // 回代  
        _coneff[n - 1] = a[n - 1, n];
        for (i = n - 1; i >= 0; i--)
        {
            _coneff[i] = a[i, n];
            for (j = i + 1; j < n; j++) _coneff[i] = _coneff[i] - a[i, j] * _coneff[j];
        }
    }

    // 选择主元素  
    private void selectMainElement(int n, int k, double[,] a)
    {
        // 寻找第k列的主元素以及它所在的行号  
        double t, mainElement;            // mainElement用于保存主元素的值  
        int l;                            // 用于保存主元素所在的行号  

        // 从第k行到第n行寻找第k列的主元素，记下主元素mainElement和所在的行号l  
        mainElement = Math.Abs(a[k, k]);  // 注意别忘了取绝对值  
        l = k;
        for (int i = k + 1; i < n; i++)
        {
            if (mainElement < Math.Abs(a[i, k]))
            {
                mainElement = Math.Abs(a[i, k]);
                l = i;                        // 记下主元素所在的行号  
            }
        }

        // l是主元素所在的行。将l行与k行交换，每行前面的k个元素都是0，不必交换  
        if (l != k)
        {
            for (int j = k; j <= n; j++)
            {
                t = a[k, j]; a[k, j] = a[l, j]; a[l, j] = t;
            }
        }
    }

    private double WaveNum_To_WaveLength(double Num)
    {
        decimal b3 = 10000000m;
        decimal b4 = 785m;

        decimal b1 = b3 * b4;
        decimal b5 = (decimal)Num;
        decimal b2 = b3 - b4 * b5;
        return (double)(b1 / b2);
    }

    /// <summary>
    /// 根据像素和波数计算校正系数
    /// </summary>
    /// <param name="pixel"></param>
    /// <param name="wavenum"></param>
    /// <param name="peakNum"></param>
    /// <returns></returns>
    public bool ReCalibrate(double[] pixel, double[] wavenum, int peakNum)
    {
        _pixel = pixel.ToArray();
        _wavenum = wavenum.ToArray();
        nPeakNumber = peakNum;

        Gauss();

        return true;
    }

    #region 波数拟合

    /// <summary>
    /// 计算波数
    /// </summary>
    /// <param name="CCD_DATA_PACK_SIZE"></param>
    public void CalWaveNum(int CCD_DATA_PACK_SIZE)
    {
        _xwavelen = new double[CCD_DATA_PACK_SIZE];

        Do_Pixel_To_Wavelength(CCD_DATA_PACK_SIZE);
    }

    private void Do_Pixel_To_Wavelength(int CCD_DATA_PACK_SIZE)
    {
        double xwavelentmp = 0;

        for (int i = 0; i < CCD_DATA_PACK_SIZE; i++)
        {
            double len = Pixel_To_WaveWavelength(i);
            if (len >= 785)
                xwavelentmp = waveLength2waveNumber(len);
            if ((xwavelentmp >= 100) && (xwavelentmp <= 8000))
            {
                _xwavelen[i] = xwavelentmp;
            }
        }
    }

    private double Pixel_To_WaveWavelength(double pixel)
    {
        int peaknum = 4;
        double bdwaveWavelength = _coneff[peaknum - 1] * pixel;
        for (int i = peaknum - 2; i > 0; i--)
            bdwaveWavelength = (bdwaveWavelength + _coneff[i]) * pixel;
        bdwaveWavelength = bdwaveWavelength + _coneff[0];
        return bdwaveWavelength;
    }

    private double waveLength2waveNumber(double length)
    {
        decimal b1 = 10000000m;
        decimal b2 = 785m;
        decimal b3 = (decimal)length;
        return (double)(b1 / b2 - b1 / b3);
    }

    #endregion

    /// <summary>
    /// Raman shift calibrate
    /// </summary>
    /// <param name="ramanShifts"></param>
    /// <returns></returns>
    public bool RamanShiftCalibrate(int[] pixels)
    {
        if (pixels == null) return false;
        if (pixels.Length < 4) return false;

        // last pixel 0 as delimeter
        var peakNum = pixels.Length + 1;
        var ramanShifts = new int[5] { 378, 918, 1374, 2252, 2943 };

        int len = 0;
        len = peakNum * 4 + 1;
        byte[] buff = new byte[len];
        buff[0] = (byte)peakNum;

        ConstructCalibrationDataPackage(ramanShifts, pixels, ref buff);

        var result_status = _dataPacketedService.Load_Calibration_Data(buff);

        if (result_status)
        {
            Calibrate();
        }

        return true;
    }

    /// <summary>
    /// Construct calibration package
    /// </summary>
    /// <param name="ramanShifts"></param>
    /// <param name="pixels"></param>
    /// <param name="buff"></param>
    private void ConstructCalibrationDataPackage(int[] ramanShifts, int[] pixels, ref byte[] buff)
    {
        var orderedPixels = pixels.Select((x, index) => new { Value = x, Index = index }).OrderBy(x => x.Value);

        var i = 0;

        foreach (var item in orderedPixels)
        {
            buff[1 + i * 4] = (byte)(item.Value >> 24);
            buff[1 + i * 4 + 1] = (byte)(item.Value >> 16);
            buff[1 + i * 4 + 2] = (byte)(item.Value >> 8);
            buff[1 + i * 4 + 3] = (byte)(item.Value);

            i++;
        }
    }
}

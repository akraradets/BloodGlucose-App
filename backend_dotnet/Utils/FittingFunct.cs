using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace backend_dotnet.Utils;

internal static class FittingFunct
{
    #region 基线校正算法最新算法

    /// <summary>
    /// 基线校正算法最新算法
    /// </summary>
    /// <param name="origin_signal_1"></param>
    /// <param name="lenght"></param>
    /// <returns></returns>
    internal static double[] NewBaselineCorrectionInt2(double[] origin_signal_1, int lenght)
    {
        int length_signal = lenght;
        //        %窗口宽度，迭代后每次增加2
        int Width_Window = 7;
        double[] baseline_signal = new double[lenght];
        //        %赋值给迭代初始值
        double[] origin_signal_itera = new double[lenght];
        for (int i = 0; i < baseline_signal.Length; i++)
        {
            baseline_signal[i] = origin_signal_1[i];
        }
        for (int i = 0; i < origin_signal_itera.Length; i++)
        {
            origin_signal_itera[i] = origin_signal_1[i];
        }


        for (int temp2 = 0; temp2 < 20; temp2++)
        {
            //% 计算要延拓的数值个数
            int start_window = (Width_Window - 1) / 2;
            //                    % 计算要延拓的数值个数
            int end_window = (Width_Window + 1) / 2;
            double[] origin_signal_itera_before = null;
            // %周期延拓，为滤波做准备
            //            for temp1 = 1:start_window    %周期延拓，为滤波做准备
            //            origin_signal_itera_before = [origin_signal_itera(1);origin_signal_itera;origin_signal_itera(length_signal)];
            //            origin_signal_itera = origin_signal_itera_before;
            //            end
            for (int temp1 = 0; temp1 < start_window; temp1++)
            {
                origin_signal_itera_before = new double[origin_signal_itera.Length + 2];
                origin_signal_itera_before[0] = origin_signal_itera[0];
                for (int j = 0; j < origin_signal_itera.Length; j++)
                {
                    origin_signal_itera_before[1 + j] = origin_signal_itera[j];
                }
                origin_signal_itera_before[origin_signal_itera_before.Length - 1] = origin_signal_itera[length_signal - 1];
                origin_signal_itera = origin_signal_itera_before;
            }




            //                    %==迭代S-G开始
            double[] origin_signal_itera_after = new double[length_signal];
            for (int temp1 = 0; temp1 < length_signal; temp1++)
            {

                //                  %==移动窗口均值滤波
                //                origin_signal_itera_after(temp1) = mean(origin_signal_itera_before(temp1:temp1+Width_Window-1));
                double[] _Temporigin_signal_itera_before = new double[temp1 + Width_Window - temp1];
                for (int j = 0; j < _Temporigin_signal_itera_before.Length; j++)
                {
                    _Temporigin_signal_itera_before[j] = origin_signal_itera_before[j + temp1];
                }
                double sum = 0;
                for (int i = 0; i < _Temporigin_signal_itera_before.Length; i++)
                    sum += _Temporigin_signal_itera_before[i];

                double average = sum / _Temporigin_signal_itera_before.Length;
                sum = 0;
                origin_signal_itera_after[temp1] = average;
            }
            //    %===计算残差===
            double[] r1 = new double[length_signal];
            for (int j = 0; j < r1.Length; j++)
            {
                r1[j] = baseline_signal[j] - origin_signal_itera_after[j];
            }

            for (int temp1 = 0; temp1 < length_signal; temp1++)
            {
                if (r1[temp1] > 0)
                {
                    baseline_signal[temp1] = origin_signal_itera_after[temp1];
                }
            }
            //            % 每次计算后，窗口宽度+4；
            Width_Window = Width_Window + 4;
            //            % 将新的基线信号参与迭代运算；
            origin_signal_itera = baseline_signal;
        }

        double[] signal_noBL = new double[length_signal];
        for (int i = 0; i < length_signal; i++)
        {
            signal_noBL[i] = origin_signal_1[i] - baseline_signal[i];
            if (signal_noBL[i] < 0)
            {
                signal_noBL[i] = 0;
            }
        }
        return signal_noBL;
    }

    #endregion
}

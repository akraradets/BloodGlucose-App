using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace backend_dotnet.Model;

/// <summary>
/// 采集方法
/// </summary>
public enum AcquireMethod
{
    /// <summary>
    /// 快速采集
    /// </summary>
    Quick = 0,

    /// <summary>
    /// 精确采集
    /// </summary>
    Precision = 1,

    /// <summary>
    /// 高精采集
    /// </summary>
    HighPrecision = 2
}

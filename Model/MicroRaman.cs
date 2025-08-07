using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace backend_dotnet.Model;

internal class MicroRaman
{
    public MotorMoveResult XState { get; set; } = MotorMoveResult.Success;
    public MotorMoveResult YState { get; set; } = MotorMoveResult.Success;
    public MotorMoveResult ZState { get; set; } = MotorMoveResult.Success;

    public bool IsMotorTurnOn { get; set; }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace backend_dotnet.Model;

public enum MotorMoveResult
{
    Success = 0,
    ReachTop = 1,
    ReachBottom = 2,
    ReachLeft = 3,
    ReachRight = 4,
    ReachForward = 5,
    ReachBackward = 6,
    Fail = 7
}

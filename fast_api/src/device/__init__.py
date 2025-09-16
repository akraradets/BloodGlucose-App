from fastapi import APIRouter, WebSocket, WebSocketDisconnect
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from datetime import datetime, timedelta
import grpc
import os
import numpy as np
from rpc import raman_pb2, raman_pb2_grpc


router = APIRouter(
    responses={
        404: {"description": "Not found (e.g., file path, resource)"},
        422: {"description": "Unprocessable Entity (e.g., invalid input format/values)"},
        500: {"description": "Internal Server Error"}
    },
)

_RPC_SERVER:str = os.environ["RPC_SERVER"]
_x_axis = np.loadtxt("static/xaxis.txt")
stub = raman_pb2_grpc.RamanStub(channel=grpc.insecure_channel(_RPC_SERVER))



class Device(BaseModel):
    name: str
    com_port: str
    device_id: str

@router.get("/", response_class=JSONResponse)
def get_device_list() -> list[Device]:
    devices:list[raman_pb2.Device] = []

    device_list_future = stub.GetDeviceList.future(request=raman_pb2.Empty())
    device_list:raman_pb2.DeviceList = device_list_future.result()
    device:raman_pb2.Device
    for device in device_list.devices:
        devices.append(device)
    # for device in device_list.devices:
    #     devices.append(device)
    
    return devices

class DeviceStatus(BaseModel):
    IsConnected: bool
    device: Device
    laser_power: int
    exposure: int
    accumulations: int
    x_axis: list[float] = _x_axis.tolist()

@router.get("/connect/{index}", response_class=JSONResponse)
def get_device_connect(index: int) -> DeviceStatus:
    status:raman_pb2.DeviceStatus = stub.Connect(raman_pb2.ConnectRequest(index=index))

    return status


@router.get("/measure_conf/{laser_power}/{exposure}/{accumulation}", response_class=JSONResponse)
def get_device_measure_conf(laser_power: int, exposure: int, accumulation: int) -> DeviceStatus:
    status:raman_pb2.DeviceStatus = stub.SetMeasureConf(raman_pb2.MeasureConfRequest(exposure=exposure, laser_power=laser_power, accumulations=accumulation))
    return status


class ConnectionManager:
    cancel:bool = False
    def __init__(self):
        self.active_connections: list[WebSocket] = []

    async def connect(self, websocket: WebSocket):
        await websocket.accept()
        self.active_connections.append(websocket)

    def disconnect(self, websocket: WebSocket):
        self.active_connections.remove(websocket)

    async def send_personal_message(self, message: str, websocket: WebSocket):
        await websocket.send_text(message)

    async def broadcast(self, message: str):
        for connection in self.active_connections:
            await connection.send_text(message)

manager = ConnectionManager()

@router.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    await manager.connect(websocket)
    try:
        while True:
            # for ccd in stub.ReadCCD(raman_pb2.Empty()):
            #     print(ccd)
            action:str = await websocket.receive_text()
            print(f"Action: {action}")

            # await manager.send_personal_message(f"Client: a", websocket)
            # await asyncio.sleep(1)
            # await manager.broadcast(f"Client #{id(websocket)} says: {data}")
    except WebSocketDisconnect:
        manager.disconnect(websocket)
        # await manager.broadcast(f"Client #{id(websocket)} left the chat")

class CCD(BaseModel):
    time: datetime
    duration: timedelta
    data: list[float]
    corrected_data: list[float]
    data_type: str


@router.websocket("/ws/ccd")
async def read_ccd(websocket: WebSocket):
    await manager.connect(websocket)
    try:
        while True:
            action:str = await websocket.receive_text()
            if(action == "read_ccd"):
                prev_data = None
                for ccd in stub.ReadCCD(raman_pb2.Empty()):
                    data = CCD(time=ccd.time.ToDatetime(), 
                               duration=timedelta(seconds=ccd.duration.seconds + ccd.duration.nanos*1e-9), 
                               data=ccd.data,
                               corrected_data=baseline_correction(np.array(ccd.data)).tolist(),
                               data_type=ccd.data_type
                               )
                    # if(data.data_type == 'signal'):
                    #     print(np.array(data.data).shape)
                    #     data.data = (np.array(data.data) - np.array(prev_data.data)).tolist()
                    #     data.corrected_data = baseline_correction(np.array(data.data)).tolist()
                    #     data.data_type = 'spectrum'
                    await manager.send_personal_message(data.model_dump_json(), websocket)
                    prev_data = data
    except WebSocketDisconnect:
        manager.disconnect(websocket)

def baseline_correction(signal: np.ndarray):
    window_size:int = 7
    baseline_signal: np.ndarray = signal.copy()
    original_signal_itera: np.ndarray = signal.copy()

    for _ in range(20):
        start_window:int = (window_size - 1) // 2
        end_window  :int = (window_size + 1) // 2
        # This is padding
        # ([a[0]] * 3) + a + ([a[-1]] * 3)
        original_signal_itera_before: list[float] = ([original_signal_itera[0]] * start_window) + original_signal_itera.tolist() + ([original_signal_itera[-1]] * end_window )
        # for temp1 in range(start_window):
        #     original_signal_itera_before.append(original_signal_itera[temp1])
        #     for j in original_signal_itera:
        #         original_signal_itera_before.append(j)
        #     original_signal_itera_before[-1] = original_signal_itera[-1]
        #     original_signal_itera = np.array(original_signal_itera_before)

        original_signal_itera_after: list[float] = []
        for temp1 in range(len(signal)):
            _Temporigin_signal_itera_before:list[float] = []
            for j in range(temp1 + window_size - temp1):
                _Temporigin_signal_itera_before.append(original_signal_itera_before[j + temp1])
            avg:float = sum(_Temporigin_signal_itera_before)/len(_Temporigin_signal_itera_before)
            original_signal_itera_after.append(avg)


        r1:np.ndarray = np.array(baseline_signal) - np.array(original_signal_itera_after)
        for idx, point in enumerate(r1):
            if point > 0:
                baseline_signal[idx] = original_signal_itera_after[idx]
        window_size = window_size + 4
        original_signal_itera = baseline_signal.copy()

    corrected_signal: np.ndarray = signal - baseline_signal
    return corrected_signal
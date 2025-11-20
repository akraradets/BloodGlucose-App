import asyncio
from fastapi import APIRouter, WebSocket, WebSocketDisconnect
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from datetime import datetime, timedelta
import grpc
import os
import numpy as np
import json
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
    corrected_data: list[float] | None = None
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

class ClientMessage(BaseModel):
    action: str

class ServerMessage(BaseModel):
    is_ok: bool
    data: dict | None = None
    message: str | None = None
    detail: str | None = None
    progress: float | None = None


async def get_raman_shift() -> list[float]:
    from src.db import OptoFile
    from datetime import datetime

    created = datetime.strptime("2025-09-25 08:16:44", "%Y-%m-%d %H:%M:%S")
    optofile = await OptoFile.fetch(subject_id='s1', created=created)
    return optofile.raman_shift

async def create_sample(spectrum:list[float]):
    from src.spectra import Sample
    import numpy as np
    from rampy import baseline as rbaseline

    raman_shift:list[float] = await get_raman_shift()
    sample:Sample = Sample(
        x=np.array(raman_shift),
        y=np.array(spectrum),
        interpolate=False,
        verbose=True
    )
    sample.despike(window_length=10,threshold=5)
    sample.interpolate(step=1)
    sample.extract_range(low=750, high=1650)
    sample.smoothing(window_length=60, polyorder=1)
    signal_y, by = rbaseline(sample.x, sample.y, roi=[[905, 915],[1050, 1070],[1100, 1150],[1400,1460]])
    sample.y = signal_y.reshape(-1)
    # f911  = sample.at(np.arange(905, 915)).mean()
    # f1060 = sample.at(np.arange(1050, 1070)).mean()
    # f1125 = sample.at(np.arange(1125, 1170)).mean()
    # f1450 = sample.at(np.arange(1440, 1460)).mean()
    sample.normalized(method='minmax')
    sample.extract_range(low=800, high=1600)
    return sample

async def predict(spectrum:list[float]) -> float:
    import pickle
    from src.spectra import Sample
    with open("../models/GridSearch-RandomForestRegressor", 'rb') as f:
        model = pickle.load(f)
    with open("../models/GridSearch-RandomForestRegressor_pca", 'rb') as f:
        pca = pickle.load(f)

    sample:Sample = await create_sample(spectrum=spectrum)
    pred = float(model.predict(pca.transform(sample.y.reshape(1,-1)))[0])
    return pred

@router.websocket("/ws/measure")
async def ws_measure(websocket: WebSocket):
    await manager.connect(websocket)
    raw:str
    try:
        while True:
            raw = await websocket.receive_text()
            cli_msg:ClientMessage = ClientMessage(**json.loads(raw))
            serv_msg:ServerMessage
            if(cli_msg.action == "arming"):
                serv_msg = ServerMessage(is_ok=True, message="Device is arming...")
            elif(cli_msg.action == "start"):
                serv_msg = ServerMessage(is_ok=True, message="Device is initializing...", progress=10.0)
                await manager.send_personal_message(serv_msg.model_dump_json(), websocket)
                devices:list[Device] = get_device_list()

                status:DeviceStatus = get_device_connect(0)
                if(status.IsConnected == False):
                    serv_msg = ServerMessage(is_ok=False, detail="Device is not connected", data={"device_status": status.model_dump()})
                    await manager.send_personal_message(serv_msg.model_dump_json(), websocket)
                    continue

                status:DeviceStatus = get_device_measure_conf(laser_power=100, exposure=2000, accumulation=3)
                # await asyncio.sleep(2)
                signal:np.ndarray = None
                for idx, progress in enumerate([30,50,70]):
                    for ccd in stub.ReadCCD(raman_pb2.Empty()):
                        data = CCD(time=ccd.time.ToDatetime(), 
                            duration=timedelta(seconds=ccd.duration.seconds + ccd.duration.nanos*1e-9), 
                            data=ccd.data,
                            data_type=ccd.data_type
                            )
                        
                        signal = np.array(ccd.data) if signal is None else signal + np.array(ccd.data)
                    serv_msg = ServerMessage(is_ok=True, message=f"Measuring sample {idx+1}", progress=progress, data={"ccd": data.model_dump()})
                    await manager.send_personal_message(serv_msg.model_dump_json(), websocket)
                serv_msg = ServerMessage(is_ok=True, message=f"Calculating...", progress=85)
                await manager.send_personal_message(serv_msg.model_dump_json(), websocket)
                glucose = await predict(signal)
                serv_msg = ServerMessage(is_ok=True, message=f"Complete", progress=100.0, data={"glucose": glucose})
            else:
                serv_msg = ServerMessage(is_ok=False, detail="Unknown action")
            await manager.send_personal_message(serv_msg.model_dump_json(), websocket)
    except json.decoder.JSONDecodeError:
        serv_msg = ServerMessage(is_ok=False, detail=f"Invalid message format | {raw=}")
        await manager.send_personal_message(serv_msg.model_dump_json(), websocket)
    except WebSocketDisconnect:
        manager.disconnect(websocket)

# async def predict(signal: np.ndarray):
#     asyncio.sleep(2)
#     return round(np.random.rand()*100,1)
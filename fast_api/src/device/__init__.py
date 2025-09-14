from fastapi import APIRouter, WebSocket, WebSocketDisconnect
from fastapi.responses import JSONResponse
from pydantic import BaseModel
import grpc
import os

from rpc import raman_pb2, raman_pb2_grpc


router = APIRouter(
    responses={
        404: {"description": "Not found (e.g., file path, resource)"},
        422: {"description": "Unprocessable Entity (e.g., invalid input format/values)"},
        500: {"description": "Internal Server Error"}
    },
)

_RPC_SERVER:str = os.environ["RPC_SERVER"]
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

@router.get("/connect/{index}", response_class=JSONResponse)
def get_device_connect(index: int) -> DeviceStatus:
    status:raman_pb2.DeviceStatus = stub.Connect(raman_pb2.ConnectRequest(index=index))
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
import asyncio
@router.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    await manager.connect(websocket)
    try:
        while True:
            # for ccd in stub.ReadCCD(raman_pb2.Empty()):
            #     print(ccd)
            action:str = await websocket.receive_text()
            print(f"Action: {action}")
            if(action == "cancel"):
                manager.cancel = True

            # await manager.send_personal_message(f"Client: a", websocket)
            # await asyncio.sleep(1)
            # await manager.broadcast(f"Client #{id(websocket)} says: {data}")
    except WebSocketDisconnect:
        manager.disconnect(websocket)
        # await manager.broadcast(f"Client #{id(websocket)} left the chat")

@router.websocket("/ws/ccd")
async def read_ccd(websocket: WebSocket):
    await manager.connect(websocket)
    try:
        action:str = await websocket.receive_text()
        if(action == "read_ccd"):
            manager.cancel = False
            for ccd in stub.ReadCCD(raman_pb2.Empty()):
                if(manager.cancel): break
                print(manager.cancel)
                await manager.send_personal_message(f"CCD: {ccd}", websocket)
    except WebSocketDisconnect:
        manager.disconnect(websocket)

from fastapi import APIRouter
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
    
    return devices
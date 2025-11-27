import unittest
import os
os.environ["RPC_SERVER"] = "backend_dotnet:5283"
from src.device import stub
from src.device.rpc import raman_pb2
# import grpc



class TestRPC(unittest.TestCase):
    def test_get_device_list(self) -> raman_pb2.DeviceList:
        response = stub.GetDeviceList(request=raman_pb2.Empty())
        self.assertIsNotNone(response)
        return response
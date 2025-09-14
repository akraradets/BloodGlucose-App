import datetime

from google.protobuf import timestamp_pb2 as _timestamp_pb2
from google.protobuf import duration_pb2 as _duration_pb2
from google.protobuf.internal import containers as _containers
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Iterable as _Iterable, Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class Empty(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class Reply(_message.Message):
    __slots__ = ("result", "detail")
    RESULT_FIELD_NUMBER: _ClassVar[int]
    DETAIL_FIELD_NUMBER: _ClassVar[int]
    result: bool
    detail: str
    def __init__(self, result: bool = ..., detail: _Optional[str] = ...) -> None: ...

class ConnectRequest(_message.Message):
    __slots__ = ("index",)
    INDEX_FIELD_NUMBER: _ClassVar[int]
    index: int
    def __init__(self, index: _Optional[int] = ...) -> None: ...

class DeviceList(_message.Message):
    __slots__ = ("devices",)
    DEVICES_FIELD_NUMBER: _ClassVar[int]
    devices: _containers.RepeatedCompositeFieldContainer[Device]
    def __init__(self, devices: _Optional[_Iterable[_Union[Device, _Mapping]]] = ...) -> None: ...

class Device(_message.Message):
    __slots__ = ("name", "com_port", "device_id")
    NAME_FIELD_NUMBER: _ClassVar[int]
    COM_PORT_FIELD_NUMBER: _ClassVar[int]
    DEVICE_ID_FIELD_NUMBER: _ClassVar[int]
    name: str
    com_port: str
    device_id: str
    def __init__(self, name: _Optional[str] = ..., com_port: _Optional[str] = ..., device_id: _Optional[str] = ...) -> None: ...

class DeviceStatus(_message.Message):
    __slots__ = ("IsConnected", "device", "laser_power", "exposure")
    ISCONNECTED_FIELD_NUMBER: _ClassVar[int]
    DEVICE_FIELD_NUMBER: _ClassVar[int]
    LASER_POWER_FIELD_NUMBER: _ClassVar[int]
    EXPOSURE_FIELD_NUMBER: _ClassVar[int]
    IsConnected: bool
    device: Device
    laser_power: int
    exposure: int
    def __init__(self, IsConnected: bool = ..., device: _Optional[_Union[Device, _Mapping]] = ..., laser_power: _Optional[int] = ..., exposure: _Optional[int] = ...) -> None: ...

class CCD(_message.Message):
    __slots__ = ("time", "duration", "data")
    TIME_FIELD_NUMBER: _ClassVar[int]
    DURATION_FIELD_NUMBER: _ClassVar[int]
    DATA_FIELD_NUMBER: _ClassVar[int]
    time: _timestamp_pb2.Timestamp
    duration: _duration_pb2.Duration
    data: _containers.RepeatedScalarFieldContainer[float]
    def __init__(self, time: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ..., duration: _Optional[_Union[datetime.timedelta, _duration_pb2.Duration, _Mapping]] = ..., data: _Optional[_Iterable[float]] = ...) -> None: ...

class MeasureConfRequest(_message.Message):
    __slots__ = ("laser_power", "exposure")
    LASER_POWER_FIELD_NUMBER: _ClassVar[int]
    EXPOSURE_FIELD_NUMBER: _ClassVar[int]
    laser_power: int
    exposure: int
    def __init__(self, laser_power: _Optional[int] = ..., exposure: _Optional[int] = ...) -> None: ...

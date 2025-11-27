from typing import Optional
from odmantic import Model, Field, Index
from datetime import datetime
from pathlib import Path
from ._var import engine

def _check_int(s:str) -> bool:
    if s[0] in ('-', '+'):
        return s[1:].isdigit()
    return s.isdigit()


class OptoFile(Model):
    subject_id: str   = Field(description='Subject ID')
    header: str       = Field(description='Header')
    file_version: str = Field(description="File Version")
    name :str         = Field(description="Name")
    creator :str      = Field(description="Creator")
    description:str   = Field(description="Description")
    created: datetime = Field(description="Created")
    integration_time: int = Field(description="Integration Time(ms)")
    laser_power: int  = Field(description="Laser Power(mW)")
    average_number:int= Field(description="Average Number")
    scan_mode:str     = Field(description="Scan Mode")
    scan_interval:int = Field(description="Scan Interval")
    device_model:str  = Field(description="Device Model")
    pixel_num:int     = Field(description="Pixel Num")
    device_sn:str     = Field(description="Device Sn")
    pretreat:Optional[str] = Field(default=None, description="Pretreat")
    pixel:Optional[list[int]]       = Field(default=None, description="Pixel")
    raman_shift:Optional[list[float]] = Field(default=None, description="Raman Shift")
    raw:Optional[list[float]]         = Field(default=None, description="Raw")
    dark:Optional[list[float]]        = Field(default=None, description="Dark")
    dark_subtracted:Optional[list[float]]     = Field(default=None, description="Dark Subtracted")
    baseline_subtracted:Optional[list[float]] = Field(default=None, description="BaseLine Subtracted")
    
    prick_time:Optional[datetime]  = Field(default=None, description="Prick Time")
    glucose_target:Optional[float] = Field(default=None, description="Glucose Target")
    is_interpolated:Optional[bool] = Field(default=False, description="Is Interpolated")

    glucose_predict:Optional[float]= Field(default=None, description="Glucose Predict")

    model_config = {
        "collection": "optofiles",
        "indexes":  lambda: [
            Index(OptoFile.subject_id, OptoFile.created, name="subject_created_index", unique=True),
        ]
    }

    @staticmethod
    async def fetch(subject_id: str, created: datetime) -> "OptoFile":
        return await engine.find_one(OptoFile, {"subject_id": subject_id, "created": created})

    @staticmethod
    async def fetch_all(subject_id: str) -> list["OptoFile"]:
        return await engine.find(OptoFile, {"subject_id": subject_id})

    async def save(self) -> None:
        await engine.save(self)

    @staticmethod
    def read_opto_file(file_path:Path, subject_id:str) -> "OptoFile":
        def rename(name:str) -> str:
            # remove space
            name = name.strip()
            # space to underscore
            name = name.replace(" ","_")
            # capital to small
            name = name.lower()
            # remove ( ) and thing in between
            while True:
                start = name.find("(")
                end = name.find(")")
                if start == -1 or end == -1 or end < start:
                    break
                name = name[:start] + name[end+1:]
            return name


        is_data = False
        with open(file_path, mode='r', encoding='utf-8-sig') as f:
            opto_data = {rename("Subject ID"):subject_id}
            for idx, line in enumerate(f.readlines()):
                line = line.strip()
                if is_data == False:
                    if(idx == 0): opto_data[rename("Header")] = line
                    elif(line[:17] == "Pixel;Raman Shift"):
                        is_data = True
                        opto_data["columns"] = [x.strip() for x in line.split(";")]
                        for col in opto_data["columns"]:
                            opto_data[rename(col)] = []
                    else:
                        key, value = line.split(";")
                        opto_data[rename(key.strip())] = value.strip()
                else:
                    values = line.split(";")
                    for col, value in zip(opto_data["columns"], values):
                        if _check_int(value.strip()):
                            opto_data[rename(col)].append(int(value.strip()))
                        else:
                            opto_data[rename(col)].append(float(value.strip()))

            opto_data[rename("Created")] = datetime.strptime(opto_data[rename("Created")], "%m/%d/%Y %H:%M:%S %p")
            opto_data[rename("Integration Time(ms)")] = int(opto_data[rename("Integration Time(ms)")])
            opto_data[rename("Laser Power(mW)")] = int(opto_data[rename("Laser Power(mW)")])
            opto_data[rename("Average Number")] = int(opto_data[rename("Average Number")])
            opto_data[rename("Scan Interval")] = int(opto_data[rename("Scan Interval")])
            opto_data[rename("Pixel Num")] = int(opto_data[rename("Pixel Num")])
        return OptoFile(**opto_data)
import requests
import json
from datetime import datetime
import enum 

class Code(enum.Enum):
    TYPICAL_MEAL  = 66
    PRE_SNACK = 64
    
    POST_SUPPER = 63
    PRE_SUPPER = 62
    
    POST_LUNCH = 61
    PRE_LUNCH = 60
    
    POST_BREAKFAST = 59
    PRE_BREAKFAST = 58

_BASE_LOCATION = "https://telehealth.ait.ac.th:5000/api/v1/"
_API_KEY = "a3fca5e7b8d1e2f3c4a5b6d7e8f9g0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7"

def fetch_by_user_id(user_id:int, start_date:datetime, end_date:datetime):
    endpoint:str = "get_blood_sugar"
    url:str = f"{_BASE_LOCATION}{endpoint}"

    payload = json.dumps({
        "patient_id": user_id,
        "date_time_start": start_date.strftime("%Y-%m-%d"),
        "date_time_end": end_date.strftime("%Y-%m-%d")
    })
    headers = {
        'Content-Type': 'application/json',
        'x-api-key': _API_KEY
    }

    response = requests.request("POST", url, headers=headers, data=payload)

    return json.loads(response.text)

def post_glucose(user_id:int, code:Code, value:float, created_date:datetime):
    endpoint:str = "diabetesrecords"
    url:str = f"{_BASE_LOCATION}{endpoint}"

    payload = json.dumps({
        "PatientID": user_id,
        "Code": code.value,
        "Value": value,
        "RecordDT": created_date.strftime("%Y-%m-%d %H:%M:%S")
    })
    headers = {
        'Content-Type': 'application/json',
        'x-api-key': _API_KEY
    }

    response = requests.request("POST", url, headers=headers, data=payload)

    return json.loads(response.text)
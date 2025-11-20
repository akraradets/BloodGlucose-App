import asyncio
from typing import Any
from fastapi import APIRouter
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from datetime import datetime
from enum import Enum

router = APIRouter(
    responses={
        404: {"description": "Not found (e.g., file path, resource)"},
        422: {"description": "Unprocessable Entity (e.g., invalid input format/values)"},
        500: {"description": "Internal Server Error"}
    },
)

class BloodCollection(BaseModel):
    name: str
    timestamp: datetime
    meal: str
    before_after: str
    glucose: float
    spectrum: list[float] | None = None
    collection_meta: dict[str, Any] | None = None

class User(Enum):
    JohnDoe = 1
    JaneSmith = 2
    BobJohnson = 3
    ASDFD = 10
    FirstnameLastname = 11
    s1 = 12

@router.post("/save", response_class=JSONResponse)
async def post_save(blood_col: BloodCollection) -> dict:
    result = collection.insert_one(blood_col.model_dump())
    return {"status": "success", "id": str(result.inserted_id), "data": blood_col}
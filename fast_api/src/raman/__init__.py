import asyncio
from typing import Any
from fastapi import APIRouter, WebSocket, WebSocketDisconnect
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from datetime import datetime
from pymongo import MongoClient

# Create a connection to the MongoDB server
client = MongoClient("mongodb://admin:password@localhost:27017/")
# Access a specific database
db = client["blood_glucose"]
# Access a specific collection within the database
collection = db["records"]

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
    spectrum: dict[str, list[float]] | None = None
    collection_meta: dict[str, Any] | None = None

@router.post("/save", response_class=JSONResponse)
async def post_save(blood_col: BloodCollection) -> dict:
    result = collection.insert_one(blood_col.model_dump())
    return {"status": "success", "id": str(result.inserted_id), "data": blood_col}
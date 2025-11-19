import os
from motor.motor_asyncio import AsyncIOMotorClient
from odmantic import AIOEngine
from typing import Any, Dict


_MONGO_URI = os.environ["ME_CONFIG_MONGODB_URL"]
_MONGO_DB = "raman"

_client:AsyncIOMotorClient[Dict[str, Any]] = AsyncIOMotorClient(_MONGO_URI)
engine = AIOEngine(client=_client, database=_MONGO_DB)

async def delete_db() -> None:
    try:
        await _client.drop_database(_MONGO_DB)
        print(f"Database '{_MONGO_DB}' dropped successfully.")
    except Exception as e:
        print(f"Error dropping database '{_MONGO_DB}': {e}")

async def close_connection() -> None:
    _client.close()
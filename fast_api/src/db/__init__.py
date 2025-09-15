import os
from pymongo import MongoClient
from typing import Any, Dict
_MONGO_URI = os.environ["ME_CONFIG_MONGODB_URL"]
_MONGO_DB = "raman"
_MONGO_COLLECTION_BLOOD = "blood"
_MONGO_COLLECTION_FINGER = "finger"
_MONGO_COLLECTION_REFERENCE = "reference"

_client:MongoClient[Dict[str, Any]] = MongoClient(_MONGO_URI)
_db = _client.get_database(_MONGO_DB)
collection_finger = _db.get_collection(_MONGO_COLLECTION_FINGER)
collection_blood  = _db.get_collection(_MONGO_COLLECTION_BLOOD)
collection_ref    = _db.get_collection(_MONGO_COLLECTION_REFERENCE)


def create_collection():
    """Create the collection"""
    # collection = db.get_collection(MONGO_BLOOD)
    collection_finger.create_index(["subject_id", "timestamp"], unique=True)
    collection_blood.create_index(["name", "timestamp"], unique=True)
    collection_ref.create_index(["name", "timestamp"], unique=True)


def reset_collection():
    """Reset the collection"""
    collection_finger.drop()
    collection_finger.create_index(["subject_id", "timestamp"], unique=True)
    collection_blood.drop()
    collection_blood.create_index(["name", "timestamp"], unique=True)
    collection_ref.drop()
    collection_ref.create_index(["name", "timestamp"], unique=True)
from contextlib import asynccontextmanager
from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles
from src.web import router as web_routers
from src.device import router as device_routers
from src.raman import router as raman_routers
import os 
_build_version = os.environ.get("BUILD_VERSION", "DEV")
@asynccontextmanager
async def lifespan(app: FastAPI):
    # All things needed to do before app is running
    yield
    # All things needed to do when shutting off


app = FastAPI(
    lifespan=lifespan,
    root_path=os.environ.get("FASTAPI_ROOT_PATH", "")
    )

app.mount("/static", StaticFiles(directory="static"), name="static")

app.include_router(web_routers)
app.include_router(device_routers, prefix="/api/device")
app.include_router(raman_routers, prefix="/api/raman")


@app.get("/")
def read_root():
    return {"Hello": "World"}


# @app.get("/items/{item_id}")
# def read_item(item_id: int, q: Union[str, None] = None):
#     return {"item_id": item_id, "q": q}
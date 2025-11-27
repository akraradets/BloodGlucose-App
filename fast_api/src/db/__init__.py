from .optofile import OptoFile
from ._var import engine


async def init_db():
    await engine.configure_database([OptoFile])
def create_app():
    from .main import app  # type: ignore
    return app
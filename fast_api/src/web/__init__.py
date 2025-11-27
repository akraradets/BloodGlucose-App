from fastapi.responses import HTMLResponse
from fastapi.templating import Jinja2Templates
from fastapi import APIRouter, HTTPException, Request
from datetime import datetime
router = APIRouter(
    responses={
        404: {"description": "Not found (e.g., file path, resource)"},
        422: {"description": "Unprocessable Entity (e.g., invalid input format/values)"},
        500: {"description": "Internal Server Error"}
    },
)
templates = Jinja2Templates(directory="templates")


@router.get("/", response_class=HTMLResponse)
async def get_homepage(request: Request) -> HTMLResponse:
    # return templates.TemplateResponse("homepage.html", {"request": request})
    return templates.TemplateResponse(name="home.html", 
                                      context={
                                          "request": request, 
                                          "date": datetime.now().strftime("%Y-%m-%d"),
                                          "time": datetime.now().strftime("%H:%M")})

@router.get("/device_control", response_class=HTMLResponse)
async def get_device_control(request: Request) -> HTMLResponse:
    # return templates.TemplateResponse("homepage.html", {"request": request})
    return templates.TemplateResponse(name="device_control.html", context={"request": request})

# async def get_item(request: Request, id: str) -> HTMLResponse:
#     return templates.TemplateResponse(
#         request=request, name="item.html", context={"id": id}
#     )

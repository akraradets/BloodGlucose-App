from fastapi.responses import HTMLResponse
from fastapi.templating import Jinja2Templates
from fastapi import APIRouter, HTTPException, Request

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
    return templates.TemplateResponse(name="home.html", context={"request": request})

@router.get("/blood", response_class=HTMLResponse)
async def get_blood(request: Request) -> HTMLResponse:
    # return templates.TemplateResponse("homepage.html", {"request": request})
    return templates.TemplateResponse(name="blood.html", context={"request": request})

# async def get_item(request: Request, id: str) -> HTMLResponse:
#     return templates.TemplateResponse(
#         request=request, name="item.html", context={"id": id}
#     )

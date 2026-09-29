import uvicorn

from makosh import config

if __name__ == "__main__":
    uvicorn.run("makosh.hub:app", host=config.HOST, port=config.PORT, reload=False)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CornersPrediction.sln ./
COPY CornersPrediction.Domain/CornersPrediction.Domain.csproj CornersPrediction.Domain/
COPY CornersPrediction.Application/CornersPrediction.Application.csproj CornersPrediction.Application/
COPY CornersPrediction.Infrastructure/CornersPrediction.Infrastructure.csproj CornersPrediction.Infrastructure/
COPY CornersPredictionApi/CornersPredictionApi.csproj CornersPredictionApi/
RUN dotnet restore CornersPredictionApi/CornersPredictionApi.csproj

COPY . .
RUN dotnet publish CornersPredictionApi/CornersPredictionApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 python3-pip python3-venv python3-dev build-essential libgomp1 \
    && rm -rf /var/lib/apt/lists/*

COPY requirements-api.txt /app/requirements-api.txt
RUN python3 -m pip install --break-system-packages --no-cache-dir -r /app/requirements-api.txt

COPY --from=build /app/publish .
COPY predict.py predict_over_under.py predict_shots_on_goal.py predict_single_model.py /app/ml/
COPY newModelsML /app/ml/newModelsML/

ENV ASPNETCORE_URLS=http://+:8080 \
    PYTHON_EXECUTABLE=python3 \
    PYTHON_PREDICT_SCRIPT_PATH=/app/ml/predict.py \
    PYTHON_SHOTS_SOG_SCRIPT_PATH=/app/ml/predict_shots_on_goal.py \
    PYTHON_SHOTS_SOG_MODEL_PATH=/app/ml/newModelsML \
    PYTHON_MODEL_DEBUG_SCRIPT_PATH=/app/ml/predict_single_model.py \
    PYTHON_MODEL_DEBUG_MODEL_PATH=/app/ml/newModelsML \
    SWAGGER_ENABLED=false

EXPOSE 8080
ENTRYPOINT ["dotnet", "CornersPredictionApi.dll"]

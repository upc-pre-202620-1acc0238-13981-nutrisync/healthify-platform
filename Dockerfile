FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
WORKDIR /app
COPY Healthify.Platform/*.csproj Healthify.Platform/
RUN dotnet restore ./Healthify.Platform
COPY . .
RUN dotnet publish ./Healthify.Platform -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=builder /app/out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Healthify.Platform.dll"]

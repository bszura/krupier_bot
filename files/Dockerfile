FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .
COPY --from=build /app/ruletka.png .
COPY --from=build /app/rira.png .
ENTRYPOINT ["dotnet", "DiscordCasinoBot.dll"]

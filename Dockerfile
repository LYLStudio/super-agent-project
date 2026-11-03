# Dockerfile for SuperAgent AI Agent CLI
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY *.sln ./
COPY src/SuperAgent.Core/SuperAgent.Core.csproj ./src/SuperAgent.Core/
COPY src/SuperAgent.Client/SuperAgent.Client.csproj ./src/SuperAgent.Client/
COPY src/SuperAgent.Console/SuperAgent.Console.csproj ./src/SuperAgent.Console/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY src/SuperAgent.Core/ ./src/SuperAgent.Core/
COPY src/SuperAgent.Client/ ./src/SuperAgent.Client/
COPY src/SuperAgent.Console/ ./src/SuperAgent.Console/

# Build the application
RUN dotnet publish ./src/SuperAgent.Console/SuperAgent.Console.csproj -c Release -o /app

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copy published output
COPY --from=build /app .

# Set environment variables (override with actual values at runtime)
ENV OPENAI_BASE_URL="http://localhost:8080"
ENV OPENAI_MODEL="qwen3-coder:30b"
ENV SUPERAGENT_ENCRYPTION_KEY="default-encryption-key"

# Expose port
EXPOSE 8080

# Entry point
ENTRYPOINT ["dotnet", "SuperAgent.Console.dll"]

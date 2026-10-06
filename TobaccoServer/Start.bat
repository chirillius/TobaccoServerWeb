echo Waiting 5min for microservices to start
timeout /t 300
TobacoServer.exe --urls http://192.168.1.220:5120;http://localhost:5120
pause
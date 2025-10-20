import asyncio
import websockets
import logging

TCP_HOST = 'localhost'
TCP_PORT = 8998 
WS_PORT = 8999

logging.basicConfig(level=logging.INFO)

async def handle_websocket(websocket, path):
    """Handle WebSocket connection and bridge to TCP"""
    logging.info("WebSocket client connected")
    
    try:
        # Connect to TCP server
        reader, writer = await asyncio.open_connection(TCP_HOST, TCP_PORT)
        logging.info(f"Connected to TCP server at {TCP_HOST}:{TCP_PORT}")
        
        # Create tasks for bidirectional communication
        async def ws_to_tcp():
            async for message in websocket:
                logging.debug(f"WS->TCP: {message}")
                writer.write(message.encode() if isinstance(message, str) else message)
                await writer.drain()
        
        async def tcp_to_ws():
            while True:
                data = await reader.readline()
                if not data:
                    break
                message = data.decode('utf-8')
                logging.debug(f"TCP->WS: {message}")
                await websocket.send(message)
        
        # Run both directions concurrently
        await asyncio.gather(ws_to_tcp(), tcp_to_ws())
        
    except Exception as e:
        logging.error(f"Bridge error: {e}")
    finally:
        writer.close()
        await writer.wait_closed()
        logging.info("Connection closed")

async def main():
    logging.info(f"Starting WebSocket-TCP bridge on port {WS_PORT}")
    async with websockets.serve(handle_websocket, "0.0.0.0", WS_PORT):
        await asyncio.Future()  # run forever

if __name__ == "__main__":
    asyncio.run(main())
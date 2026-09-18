#!/usr/bin/env python3
"""
Simple connection test for Unity GymServer.
Tests TCP connection and get_game_state request.
"""

import socket
import json
import sys

def test_connection(host="localhost", port=9876):
    """Test basic TCP connection to Unity."""
    print(f"🔌 Testing connection to {host}:{port}...")
    
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5.0)
        sock.connect((host, port))
        print(f"✅ Connected!")
        
        # Send get_game_state request
        request = {"type": "get_game_state"}
        sock.sendall((json.dumps(request) + "\n").encode())
        print(f"📤 Sent: get_game_state")
        
        # Receive response
        response = b''
        while not response.endswith(b'\n'):
            response += sock.recv(4096)
        
        data = json.loads(response.decode().strip())
        print(f"📥 Response type: {data.get('type')}")
        
        if data.get('type') == 'error':
            print(f"❌ Error: {data.get('error')}")
            return False
            
        # Parse game state
        game_state = json.loads(data.get('game_state', '{}'))
        session = game_state.get('sessionInfo', {})
        sat_budget = game_state.get('satisfactionAndBudget', {})
        
        print(f"\n✅ Game State:")
        print(f"   Day: {session.get('currentDay', '?')}")
        print(f"   Segment: {session.get('currentTimeSegment', '?')}")
        print(f"   Satisfaction: {sat_budget.get('satisfaction', '?')}")
        print(f"   Budget: ${sat_budget.get('budget', '?'):,}")
        
        sock.close()
        print(f"\n🎉 SUCCESS! Unity GymServer is working.")
        return True
        
    except ConnectionRefusedError:
        print(f"❌ Connection refused")
        print(f"\n💡 To fix:")
        print(f"   1. Open Unity Editor")
        print(f"   2. Open MainScene")
        print(f"   3. Click Play (GymServerInitializer will auto-create the server)")
        print(f"   4. Look for: [GymServer] ✅ Gym server listening on port 9876")
        return False
    except Exception as e:
        print(f"❌ Error: {e}")
        return False

if __name__ == "__main__":
    success = test_connection()
    sys.exit(0 if success else 1)

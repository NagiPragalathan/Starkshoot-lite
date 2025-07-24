import requests
import json

url = "https://starkshoot-server.vercel.app/api/leaderboard/add"

# Sample data similar to your C# code structure
data = {
    "walletAddress": "0x123abc456def789",
    "kills": 10,
    "score": 1500,
    "roomId": "room_42",
    "username": "PlayerOne"
}

headers = {
    "Content-Type": "application/json"
}

response = requests.post(url, headers=headers, data=json.dumps(data))

if response.status_code == 200:
    response_data = response.json()
    print(f"Successfully added leaderboard entry. Entry ID: {response_data.get('_id')}")
else:
    print(f"Failed to add leaderboard entry. Status code: {response.status_code}, Error: {response.text}")

import requests

# Define the wallet address
wallet_address = "0x06C3431c2D3F57BfE4de3A99Af9B53fc4f95197c"

# Define the API URL
url = f"https://starkshoot-server.vercel.app/api/user/{wallet_address}"

# Send the GET request
response = requests.get(url)

# Check if the request was successful
if response.status_code == 200:
    print("User details retrieved successfully!")
    print(response.json())  # Print the response JSON
elif response.status_code == 404:
    print("User not found.")
else:
    print(response.json())
    print(f"Failed to retrieve user details. Status code: {response.status_code}")

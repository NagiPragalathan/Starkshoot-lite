from flask import Flask, send_from_directory, request, make_response, send_file
from flask_compress import Compress
import os
import mimetypes

app = Flask(__name__, static_folder='.')
Compress(app)

# Set cache headers for Unity build files
CACHE_EXTENSIONS = ('.wasm', '.data', '.js', '.css', '.png', '.jpg', '.jpeg', '.ico')

mimetypes.add_type('application/wasm', '.wasm')
mimetypes.add_type('application/octet-stream', '.data')
mimetypes.add_type('application/javascript', '.js')

def set_cache_headers(response, filename):
    if filename.endswith(CACHE_EXTENSIONS):
        response.headers['Cache-Control'] = 'public, max-age=31536000, immutable'
    else:
        response.headers['Cache-Control'] = 'no-cache'
    return response

@app.route('/')
def index():
    return send_file('index.html')

@app.route('/storage.js')
def storage_js():
    response = make_response(send_file('storage.js'))
    response.headers['Content-Type'] = 'application/javascript'
    return response

@app.route('/sw.js')
def service_worker():
    response = make_response(send_file('sw.js'))
    response.headers['Content-Type'] = 'application/javascript'
    return response

@app.route('/Build/<path:filename>')
def build_files(filename):
    response = make_response(send_from_directory('Build', filename))
    return set_cache_headers(response, filename)

@app.route('/TemplateData/<path:filename>')
def template_data_files(filename):
    response = make_response(send_from_directory('TemplateData', filename))
    return set_cache_headers(response, filename)

# Serve favicon
@app.route('/favicon.ico')
def favicon():
    return send_from_directory('TemplateData', 'favicon.ico')

@app.route('/<path:path>')
def serve_file(path):
    response = make_response(send_file(path))
    
    # Set correct headers for caching
    if path.endswith(('.data', '.wasm', '.js')):
        response.headers['Cache-Control'] = 'public, max-age=31536000, immutable'
        response.headers['Access-Control-Allow-Origin'] = '*'
        
        # Set correct content type
        if path.endswith('.wasm'):
            response.headers['Content-Type'] = 'application/wasm'
        elif path.endswith('.js'):
            response.headers['Content-Type'] = 'application/javascript'
        elif path.endswith('.data'):
            response.headers['Content-Type'] = 'application/octet-stream'
    
    return response

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=80)
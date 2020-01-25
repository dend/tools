import heatmap
from scipy import ndimage
from skimage import io
import numpy as np
import matplotlib.pyplot as plt
import json
import pandas as pd

# Read image for the overlay.
image_filename = 'docs.jpg'
image = io.imread(image_filename)

plt.figure(figsize=(250,100))

# Create array of zero-values, that aligns with the size of the image.
heat_set = np.zeros((480, 120))

data = []
with open('button-map.json') as button_map:
    data = json.load(button_map)

data_frame = pd.read_csv('hero-clicks.csv')

bound_min, bound_max = 1, 25
current_min, current_max = data_frame.clicks.min(), data_frame.clicks.max()

data_frame['normal'] = (data_frame.clicks - current_min) / (current_max - current_min) * (bound_max - bound_min) + bound_min

data_frame.head()

for index, row in data_frame.iterrows():
    entry = next(d for d in data if d['button'] == row['Target'])
    
    heat_set[entry['coordinates'][1], entry['coordinates'][0]] = row['normal']

heat_map = ndimage.filters.gaussian_filter(heat_set, sigma=5)
heatmap.add(image, heat_map, alpha=0.8, save='face_heat_map.png')

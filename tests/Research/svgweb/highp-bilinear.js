(() => {
  const rows = [];
  for (const axis of ['x', 'y']) {
    for (const edge of [4, 8, 16, 32]) {
      for (const belowSteps of [1, 2]) {
        const bits = new Uint32Array(new Float32Array([edge]).buffer);
        bits[0] -= belowSteps;
        const coordinate = new Float32Array(bits.buffer)[0];
        const source = document.createElement('canvas');
        source.width = axis === 'x' ? edge + 3 : 4;
        source.height = axis === 'y' ? edge + 3 : 4;
        const sourceContext = source.getContext('2d');
        sourceContext.fillStyle = 'white';
        sourceContext.fillRect(axis === 'x' ? edge : 0, axis === 'y' ? edge : 0,
          axis === 'x' ? 1 : source.width, axis === 'y' ? 1 : source.height);
        const target = document.createElement('canvas');
        target.width = target.height = 1;
        const context = target.getContext('2d', {willReadFrequently: true});
        context.globalCompositeOperation = 'color-dodge';
        context.imageSmoothingEnabled = true;
        context.drawImage(source, axis === 'x' ? 0.5 - coordinate : -1,
          axis === 'y' ? 0.5 - coordinate : -1);
        rows.push({axis, edge, belowSteps, coordinate,
          rgba: Array.from(context.getImageData(0, 0, 1, 1).data)});
      }
    }
  }
  return rows;
})()

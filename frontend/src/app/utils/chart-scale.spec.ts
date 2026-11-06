import { buildLinePath, linearScale, markerPath, niceScale, pickSpreadIndexes, pointPositions } from './chart-scale';

describe('chart-scale', () => {
  describe('niceScale', () => {
    it('returns a 0-1 scale for zeros, negatives and invalid values', () => {
      for (const value of [0, -5, NaN, Infinity]) {
        expect(niceScale(value)).toEqual({ max: 1, ticks: [0, 1] });
      }
    });

    it('rounds the maximum up to a multiple of a 1-2-5 step', () => {
      expect(niceScale(7)).toEqual({ max: 8, ticks: [0, 2, 4, 6, 8] });
      expect(niceScale(100)).toEqual({ max: 100, ticks: [0, 50, 100] });
      expect(niceScale(130).max).toBeGreaterThanOrEqual(130);
      expect(niceScale(9400).max).toBeGreaterThanOrEqual(9400);
    });

    it('keeps integer steps for small counts', () => {
      const scale = niceScale(1);
      expect(scale.ticks.every(Number.isInteger)).toBe(true);
      expect(scale.max).toBeGreaterThanOrEqual(1);
      expect(scale.ticks[0]).toBe(0);
      expect(scale.ticks[scale.ticks.length - 1]).toBe(scale.max);
    });

    it('always starts at 0, ends at max and never has more than ~6 ticks', () => {
      for (const value of [1, 2, 3, 9, 10, 11, 47, 99, 101, 1234, 56789]) {
        const { max, ticks } = niceScale(value);
        expect(ticks[0]).toBe(0);
        expect(ticks[ticks.length - 1]).toBe(max);
        expect(max).toBeGreaterThanOrEqual(value);
        expect(ticks.length).toBeLessThanOrEqual(7);
      }
    });
  });

  describe('linearScale', () => {
    it('maps the domain onto the range, also inverted (SVG y axis)', () => {
      const y = linearScale([0, 10], [100, 0]);
      expect(y(0)).toBe(100);
      expect(y(10)).toBe(0);
      expect(y(5)).toBe(50);
    });

    it('returns the middle of the range for a degenerate domain', () => {
      expect(linearScale([3, 3], [0, 100])(3)).toBe(50);
    });
  });

  describe('pointPositions', () => {
    it('spreads points evenly including both ends', () => {
      expect(pointPositions(3, 10, 30)).toEqual([10, 20, 30]);
    });

    it('centres a single point and handles no points', () => {
      expect(pointPositions(1, 10, 30)).toEqual([20]);
      expect(pointPositions(0, 10, 30)).toEqual([]);
    });
  });

  describe('buildLinePath', () => {
    it('builds an M/L path', () => {
      expect(buildLinePath([0, 10, 20], [5, 1, 3])).toBe('M0 5 L10 1 L20 3');
    });

    it('returns an empty path for an empty series and a lone move for one point', () => {
      expect(buildLinePath([], [])).toBe('');
      expect(buildLinePath([50], [20])).toBe('M50 20');
    });

    it('draws a flat line for all zeros and rounds long decimals', () => {
      const zeros = linearScale([0, 1], [200, 0]);
      expect(buildLinePath([0, 10], [zeros(0), zeros(0)])).toBe('M0 200 L10 200');
      expect(buildLinePath([0.123456, 1], [1 / 3, 2])).toBe('M0.12 0.33 L1 2');
    });

    it('ignores surplus coordinates', () => {
      expect(buildLinePath([0, 1, 2], [0, 1])).toBe('M0 0 L1 1');
    });
  });

  describe('pickSpreadIndexes', () => {
    it('returns all indexes when they fit', () => {
      expect(pickSpreadIndexes(4, 7)).toEqual([0, 1, 2, 3]);
    });

    it('spreads at most `max` indexes including first and last', () => {
      const indexes = pickSpreadIndexes(366, 7);
      expect(indexes.length).toBe(7);
      expect(indexes[0]).toBe(0);
      expect(indexes[indexes.length - 1]).toBe(365);
      expect([...indexes].sort((a, b) => a - b)).toEqual(indexes);
    });

    it('handles empty, single and max=1 inputs', () => {
      expect(pickSpreadIndexes(0, 5)).toEqual([]);
      expect(pickSpreadIndexes(1, 5)).toEqual([0]);
      expect(pickSpreadIndexes(10, 1)).toEqual([0]);
      expect(pickSpreadIndexes(10, 0)).toEqual([]);
    });
  });

  describe('markerPath', () => {
    it('draws a different closed outline for each shape', () => {
      const shapes = (['circle', 'square', 'diamond', 'triangle'] as const).map(shape => markerPath(shape, 10, 10, 4));
      expect(new Set(shapes).size).toBe(4);
      shapes.forEach(path => expect(path.endsWith('Z')).toBe(true));
    });
  });
});

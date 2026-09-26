import {expect, test} from "bun:test";
import {overviewMarkerRanges} from "./preview";

test("overview markers merge duplicate and adjacent rendered change rows", () => {
    expect(overviewMarkerRanges([
        {top: 100, height: 20},
        {top: 100, height: 20},
        {top: 120, height: 20},
        {top: 400, height: 20},
    ], .1)).toEqual([
        {top: 10, bottom: 14, target: 100},
        {top: 40, bottom: 42, target: 400},
    ]);
});

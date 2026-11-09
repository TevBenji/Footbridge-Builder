// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
// The design picker's pictures: each shape from above, over a road.
import Auto from "../assets/designs/Auto.svg";
import YRamps from "../assets/designs/YRamps.svg";
import ZRamps from "../assets/designs/ZRamps.svg";
import Switchback from "../assets/designs/Switchback.svg";
import SpiralRamp from "../assets/designs/SpiralRamp.svg";
import DiagonalSpan from "../assets/designs/DiagonalSpan.svg";
import SnakeDeck from "../assets/designs/SnakeDeck.svg";
import { Design } from "./state";

export const DESIGN_ICONS: Record<Design, string> = { Auto, YRamps, ZRamps, Switchback, SpiralRamp, DiagonalSpan, SnakeDeck };

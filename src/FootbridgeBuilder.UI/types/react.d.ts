// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: declarations for the Footbridge Builder UI; normalized whitespace.
import * as React from 'react';

declare module 'react' {
    interface HTMLAttributes<T> extends React.AriaAttributes, React.DOMAttributes<T> {
      // extends React's HTMLAttributes
      cohinline?: string;
    }
}
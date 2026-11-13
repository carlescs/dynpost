import { ResponseDto } from '../../../../models/messages';
import { previewKind, withPreviewPolicy } from './response-preview';

const response = (patch: Partial<ResponseDto>): ResponseDto => ({
  status: 200,
  statusText: 'OK',
  headers: [],
  contentType: 'text/html',
  body: '<p>hi</p>',
  bodyEncoding: 'text',
  size: 9,
  truncated: false,
  durationMs: 1,
  ...patch,
});

describe('response preview', () => {
  describe('previewKind', () => {
    it('previews HTML text bodies', () => {
      expect(previewKind(response({}))).toBe('html');
      expect(previewKind(response({ contentType: 'application/xhtml+xml' }))).toBe('html');
      expect(previewKind(response({ body: '' }))).toBeNull();
      expect(previewKind(response({ contentType: 'application/json' }))).toBeNull();
    });

    it('previews complete PDF bodies only', () => {
      const pdf = { contentType: 'application/pdf', bodyEncoding: 'base64' as const, body: 'JVBE' };
      expect(previewKind(response(pdf))).toBe('pdf');
      expect(previewKind(response({ ...pdf, truncated: true }))).toBeNull();
      expect(previewKind(response({ ...pdf, bodyEncoding: 'text' }))).toBeNull();
    });
  });

  describe('withPreviewPolicy', () => {
    const meta = /^<meta http-equiv="Content-Security-Policy" content="[^"]+">/;

    it('puts the policy before the page', () => {
      const html = withPreviewPolicy('﻿<p>hi</p>');
      expect(html).toMatch(meta);
      expect(html.endsWith('><p>hi</p>')).toBe(true);
    });

    it('keeps a doctype first', () => {
      const html = withPreviewPolicy('<!DOCTYPE html><html></html>');
      expect(html.startsWith('<!DOCTYPE html><meta http-equiv="Content-Security-Policy"')).toBe(
        true,
      );
      expect(html.endsWith('<html></html>')).toBe(true);
    });

    it('blocks network requests from the page', () => {
      expect(withPreviewPolicy('')).toContain("default-src 'none'");
    });
  });
});

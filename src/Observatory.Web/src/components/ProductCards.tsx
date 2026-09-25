import { useState } from 'react';
import type { Product } from '../contracts';
import { money, publicThumbnail } from '../lib/format';
import { redactText } from '../lib/redaction';
import { Icon } from './Common';

export function ProductCard({ product, onAsk, compact = false, showDetails = false }: {
  product: Product; onAsk?: (product: Product) => void; compact?: boolean; showDetails?: boolean;
}) {
  const [failed, setFailed] = useState(false);
  const image = publicThumbnail(product.thumbnail);
  return <article className={`product-card ${compact ? 'product-card-compact' : ''}`}>
    <div className="product-image">
      {image && !failed
        ? <img src={image} alt={product.title} loading="lazy" decoding="async" referrerPolicy="no-referrer" onError={() => setFailed(true)} />
        : <span className="image-unavailable">Immagine non disponibile</span>}
      <span className="product-id">#{product.id}</span>
    </div>
    <div className="product-content">
      <p className="product-category">{redactText(product.category)}</p>
      <h3>{redactText(product.title)}</h3>
      <div className="product-price"><strong>{money(product.price, product.currency)}</strong><span>{product.stock} nel dataset</span></div>
      {!compact && <p className="product-description">{redactText(product.description)}</p>}
      <p className="product-notice">Listino pubblico · non è l’importo pagato</p>
      {showDetails && !compact && <details className="product-details">
        <summary>Dettagli completi · #{product.id}</summary>
        <p>{redactText(product.description)}</p>
        <dl><dt>Marca</dt><dd>{product.brand ? redactText(product.brand) : 'Non disponibile'}</dd>
          <dt>SKU</dt><dd>{redactText(product.sku)}</dd><dt>Valuta</dt><dd>{product.currency}</dd>
          <dt>Tag</dt><dd>{product.tags.length ? product.tags.map(redactText).join(', ') : 'Nessun tag'}</dd>
          <dt>Thumbnail</dt><dd>{product.thumbnail ? redactText(product.thumbnail) : 'Non disponibile'}</dd>
          <dt>Immagini nel catalogo</dt><dd>{product.images.length ? <ul>{product.images.map((url, index) => {
            const safeUrl = publicThumbnail(url);
            return <li key={`${index}-${url}`}>{safeUrl
              ? <a href={safeUrl} target="_blank" rel="noopener noreferrer" referrerPolicy="no-referrer">Immagine {index + 1}</a>
              : 'URL immagine non consentito'}</li>;
          })}</ul> : 'Nessuna immagine'}</dd>
        </dl>
      </details>}
      {onAsk && <button className="button button-small button-quiet" type="button" onClick={() => onAsk(product)} aria-label={`Prepara una domanda su ${product.title}, prodotto ${product.id}`}><Icon name="plus" />Chiedi del prodotto</button>}
    </div>
  </article>;
}

export function ResultProducts({ ids, products, onAsk }: {
  ids: readonly number[]; products: readonly Product[]; onAsk?: (product: Product) => void;
}) {
  const uniqueIds = [...new Set(ids)];
  if (uniqueIds.length === 0) return null;
  const selected = products.filter((product) => uniqueIds.includes(product.id));
  const missing = uniqueIds.filter((id) => !products.some((product) => product.id === id));
  return <div className="result-products">
    <p className="small-label">Prodotti citati dal backend · immagini aggiunte soltanto nella UI</p>
    <div className="product-grid product-grid-result">{selected.map((product) => <ProductCard key={product.id} product={product} compact {...(onAsk ? { onAsk } : {})} />)}</div>
    {missing.length > 0 && <p className="muted">Prodotti {missing.map((id) => `#${id}`).join(', ')}: non presenti nel catalogo caricato.</p>}
  </div>;
}

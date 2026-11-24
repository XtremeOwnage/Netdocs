// Hash router: #/screen/sub?query
import { useEffect, useState } from 'preact/hooks';

export interface Route { path: string; params: URLSearchParams }

const parse = (): Route => {
  const hash = location.hash.replace(/^#/, '') || '/';
  const [path, query] = hash.split('?');
  return { path: path || '/', params: new URLSearchParams(query ?? '') };
};

export function useRoute(): Route {
  const [route, setRoute] = useState(parse);
  useEffect(() => {
    const on = () => setRoute(parse());
    window.addEventListener('hashchange', on);
    return () => window.removeEventListener('hashchange', on);
  }, []);
  return route;
}

export const navigate = (to: string) => { location.hash = to; };

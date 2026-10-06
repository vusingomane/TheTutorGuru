import express, { type Express } from "express";
import cors from "cors";
import pinoHttp from "pino-http";
import router from "./routes";
import { logger } from "./lib/logger";
import { clerkMiddleware } from "@clerk/express";
import { publishableKeyFromHost } from "@clerk/shared/keys";
import { CLERK_PROXY_PATH, clerkProxyMiddleware, getClerkProxyHost } from "./middlewares/clerkProxyMiddleware";
import { HttpError } from "./lib/tutoring";

const app: Express = express();

app.use(
  pinoHttp({
    logger,
    serializers: {
      req(req) {
        return {
          id: req.id,
          method: req.method,
          url: req.url?.split("?")[0],
        };
      },
      res(res) {
        return {
          statusCode: res.statusCode,
        };
      },
    },
  }),
);
app.use(CLERK_PROXY_PATH, clerkProxyMiddleware());
app.use(cors({ credentials: true, origin: true }));
app.use(express.json());
app.use(express.urlencoded({ extended: true }));
app.use(clerkMiddleware((req) => ({
  publishableKey: publishableKeyFromHost(getClerkProxyHost(req) ?? "", process.env.CLERK_PUBLISHABLE_KEY),
})));

app.use("/api", router);
app.use((err: Error & { code?: string; issues?: unknown }, req: express.Request, res: express.Response, _next: express.NextFunction) => {
  if (err instanceof HttpError) { res.status(err.status).json({error:err.message}); return; }
  if (err.name === "ZodError" || err.code === "22P02") { res.status(400).json({error:"Check the form values and try again."}); return; }
  if (err.code === "23505") { res.status(409).json({error:"This record already exists."}); return; }
  req.log.error({err},"Request failed");
  res.status(500).json({error:"Something went wrong. Please try again."});
});

export default app;
